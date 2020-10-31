 /*
 * HTMIndexSample.sql -- SQL script demonstrating the usage of the HTM index for point classification.
 *
 *
 * Copyright 2014 Kondor Dániel <kondor.dani@gmail.com>
 *   http://www.vo.elte.hu/htmpaper/
 * 
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are
 * met:
 * 
 * * Redistributions of source code must retain the above copyright
 *   notice, this list of conditions and the following disclaimer.
 * * Redistributions in binary form must reproduce the above
 *   copyright notice, this list of conditions and the following disclaimer
 *   in the documentation and/or other materials provided with the
 *   distribution.
 * * Neither the name of the  nor the names of its
 *   contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 * "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 * A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
 * OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
 * LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 * DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 * THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
 * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 * 
 */


use dkondor
go



/********************
 * 1. Sample data
 ********************/

-- 1.1 points to classify 
create table samplepts(htmid bigint not null, ptid bigint not null, lon float not null, lat float not null,
	primary key(htmid,ptid)) -- make the htmid the primary key, to support efficient searches later
-- generate some data; pts1 should contain ids and coordinates, the fHtmEq() function is from the HTM library
insert into samplepts with(tablockx)
select sph.fHtmEq(lon,lat), ptid, lon, lat from pts1 tablesample(1000000 rows) 



-- 1.2 regions
create table sampleregions(ID smallint not null, Geom geography not null)
insert into sampleregions with(tablockx)
select ID, Geom from GAdm.dbo.Region where ID in (3411,3419) -- Colorado and Illinois are given as an example; all of the regions should be used here


/***********************
 * 2. Create the index
 ***********************/

-- 2.1 calculate the covering
create table samplehtmindex(ID smallint not null, lo bigint not null, hi bigint not null, [full] bit not null)
insert into samplehtmindex with(tablockx)
select ID, lo, hi, [full]
	from sampleregions cross apply dbo.HTMIndexCreate(Geom,12,0) -- only level 12 index is created as an example; level 14 or 16 is recommended for country-level indexes

-- 2.2 create an index on the table to speed up spatial joins
create clustered index ix_samplehtmindex on samplehtmindex([full],lo,hi)


/**************************************
 * 3. classify points in full trixels 
 **************************************/

create table sampleptsfull(ptid bigint not null, region_id smallint not null)
insert into sampleptsfull with(tablockx)
select ptid, ID from samplehtmindex inner loop join samplepts on [full] = 1 and
	htmid between lo and hi -- note: the order of the tables is important for the join to run efficiently
	-- also, the 'loop join' hint is needed in some cases


/*****************************************
 * 4. classify points in partial trixels
 *****************************************
 * 4.A.: simpler, but slower method
 *****************************************/

-- 4.A1 store all points from partial trixels into a temporary table as geography objects
create table sampleptspartial1(ptid bigint not null, region_id smallint not null, point geography not null)
insert into sampleptspartial1 with(tablockx)
select ptid, ID, geography::Point(lat,lon,4326) from samplehtmindex
	inner loop join samplepts on [full] = 0 and htmid between lo and hi

-- 4.A2 use the SQL Server geography methods to test for containment with the candidate regions
--	(note: using SQL Server geography indexes could speed up this step; this is omitted here)
create table sampleptspartialA(ptid bigint not null, region_id smallint not null)
insert into sampleptspartialA with(tablockx)
select ptid, region_id from sampleptspartial1 join sampleregions on
	region_id = ID where Geom.STIntersects(point) = 1


/****************************************
 * 4.B.: more complex method:
 *	first, intersect the partial trixels and the regions,
 *	and use these intersections in the containment tests
 ****************************************/

-- 4.B1 store all points in a temporary table + also store the level 12 HTM IDs of the partial cells (note that we use the fact that lo / 65536 == hi / 65536
--	for partial cells, since these are at least level 12 cells)
create table sampleptspartial2(ptid bigint not null, region_id smallint not null, htmid bigint not null, point geography not null)
insert into sampleptspartial2 with(tablockx)
select ptid, ID, lo / 65536, geography::Point(lat,lon,4326) from samplehtmindex
	inner loop join samplepts on [full] = 0 and htmid between lo and hi

-- 4.B2 create the intersections (normally, we could use indexes for this); note that we create intersections only for
--	trixels which contain at least one point
create table sampleintersections(region_id smallint not null, htmid bigint not null, intersection geography not null);
with a as (select distinct region_id, htmid from sampleptspartial2)
insert into sampleintersections select region_id, htmid, Geom.STIntersection(dbo.GeomFromTrixel(htmid))
from a join sampleregions on region_id = ID


-- 4.B3 perform containment tests using the intersections
create table sampleptspartialB(ptid bigint not null, region_id smallint not null)
insert into sampleptspartialB with(tablockx)
select ptid, p.region_id from sampleptspartial2 p join sampleintersections i on p.htmid = i.htmid
	and p.region_id = i.region_id where intersection.STIntersects(point) = 1
go




/*****************************************************************
 * 5. simplified approach: create a stored procedure
 *	which carries out the above task (3. and 4. together);
 *	this makes carrying out the classifications easier
 *	(simpler query), although the performance may be worse,
 *	when running for a large number of points, it is advised
 *	to use the above queries
 *****************************************************************/
-- 5.0 create a table definition which can be used when defining the stored procedures
create type ptsparams as table(id bigint not null, htmid bigint not null, lat float not null, lon float not null,
	primary key(htmid,id))
go


/********************************************
 * 5.A slower but simpler method
 ********************************************/

-- 5.A.1 create the stored procedure
create proc classifypointssimple(@pts ptsparams readonly) as
begin
	declare @ptsreg table(id bigint not null, region_id smallint not null)
	insert into @ptsreg
		select p.id, r.ID
		from samplehtmindex r inner loop join @pts p on [full] = 1 and htmid between lo and hi
	declare @ptspartial table(id bigint not null, region_id smallint not null, point geography not null)
	insert into @ptspartial
		select p.id, r.ID, geography::Point(lat,lon,4326) as point
		from samplehtmindex r inner loop join @pts p on [full] = 0 and htmid between lo and hi
	insert into @ptsreg
		select p.id, p.region_id
		from @ptspartial p join sampleregions r on p.region_id = r.ID
		where r.Geom.STIntersects(p.point) = 1
	select * from @ptsreg
end
go


-- 5.A.2. test run; using the stored procedure, classifying points can be
--	carried out with a relatively easy query
declare @cpts1 ptsparams
insert into @cpts1
select ptid, htmid, lat, lon from samplepts
create table cltest5A(id bigint not null, region_id smallint not null)
insert into cltest5A with(tablockx) exec dbo.classifypointssimple @cpts1
go



/********************************************
 * 5.B improved method (corresponds to 4.B)
 ********************************************/

-- 5.B.1 create the stored prodecure
create proc classifypointsadvanced(@pts ptsparams readonly) as
begin
	declare @ptsreg table(id bigint not null, region_id smallint not null)
	insert into @ptsreg 
		select p.id, r.ID
		from samplehtmindex r inner loop join @pts p on [full] = 1 and htmid between lo and hi
	-- note: we use a temporary table here instead of a table variable, as it enables creating indexes, which will
	--	speed up processing when we have a large number of points
	create table #ptspartial(id bigint not null, region_id smallint not null, htmid bigint not null, point geography not null)
	insert into #ptspartial
		select p.id, r.ID, lo / 65536, geography::Point(lat,lon,4326) as point
		from samplehtmindex r inner loop join @pts p on [full] = 0 and htmid between lo and hi
	create clustered index ix_ptspartial on #ptspartial(htmid, region_id)
	declare @pcells table(htmid bigint not null, region_id smallint not null, geomint geography not null,
		primary key(htmid,region_id));
	with a as (select distinct htmid as htm12, region_id from #ptspartial)
	insert into @pcells select htm12, region_id, Geom.STIntersection(dkondor.dbo.GeomFromTrixel(htm12)) from a
		join sampleregions r on a.region_id = r.ID
	insert into @ptsreg
		select p.id, p.region_id
		from #ptspartial p join @pcells r on p.region_id = r.region_id
			and p.htmid = r.htmid
		where r.geomint.STIntersects(p.point) = 1
	drop table #ptspartial
	select * from @ptsreg
end
go


-- 5.B.2. test run
declare @cpts1 ptsparams
insert into @cpts1
select ptid, htmid, lat, lon from samplepts
create table cltest5B(id bigint not null, region_id smallint not null)
insert into cltest5B with(tablockx) exec classifypointsadvanced @cpts1
go

