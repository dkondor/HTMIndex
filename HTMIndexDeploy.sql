 /*
 * HTMIndexDeploy.sql -- SQL script demonstrating the deployment of the HTM indexing library to a
 *	Microsoft SQL Server database server.
 *
 *
 * Copyright 2014 Kondor Dániel <kondor.dani@gmail.com>
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
/*
 1. Load the assemblies manually, the following are needed:
Spherical Toolkit (http://voservices.net/spherical):
	SphericalLib.dll
	SphericalHtm.dll
	Spherical.Quickhull.dll
HTM indexing library (http://www.vo.elte.hu/htmpaper/):
	HTMIndex.dll
*/


/*
 2. Create the main function implementing the indexing
parameters:
	@geom: region to be indexed
	@level: maximum HTM depth to use for the index
	@partialint: if set to 1, also return the intersections of partial trixels with the regions
return:
	HTM ranges covering the region (lo and hi: level 20 ranges)
	full: 1, for full trixels (trixels which are fully contained in the region), 0 for partial trixels (trixels intersection with the region)
	geomint: intersection of the trixel with the region (only for partial trixels, if the partialint parameter = 1; null otherwise)
*/
create function dbo.HTMIndexCreate(@geom geography, @level smallint, @partialint bit)
returns table(lo bigint, hi bigint, [full] bit, geomint geography)
external name 
	HTMIndex.[HTMIndex.HTMIndex].HTMIndexCreate
go


/*
 3. Other functions
 */

/* index creation with more options:
	shrinkeps: shrink factor for trixel containment tests to avoid round-off errors
	chulllevel: HTM level to use for the starting convex hull
*/
create function dbo.HTMIndexCreate2(@geom geography, @level smallint, @shrinkeps double precision, @chulllevel smallint, @partialint bit)
returns table(lo bigint, hi bigint, [full] bit, geomint geography)
external name 
	HTMIndex.[HTMIndex.HTMIndex].HTMIndexCreate2
go


-- create a convex hull of a region and cover it with trixels of a given maximum level
create function dbo.GeomToHTMChull(@geom geography, @level smallint)
returns table(lo bigint, hi bigint)
external name 
	HTMIndex.[HTMIndex.HTMIndex].GeomToHTMChull
go


-- create a geography object representing a single trixel
create function dbo.GeomFromTrixel(@htmid bigint)
returns geography
external name 
	HTMIndex.[HTMIndex.HTMIndex].GeomFromTrixel
go


-- create a geography object representing a single trixel, shrunk by a given factor
--	(i.e. all points are moved towards the center with a factor of eps)
create function dbo.GeomFromTrixelEps(@htmid bigint, @eps double precision)
returns geography
external name 
	HTMIndex.[HTMIndex.HTMIndex].GeomFromTrixelEps
go


-- truncate a trixel to a lower depth
create function dbo.fHtmTruncate(@hid bigint, @level smallint)
returns bigint external name HTMIndex.[HTMIndex.HTMIndex].fHtmTruncate
go

-- extend a trixel to a higher depth
create function dbo.fHtmExtend(@hid bigint, @level smallint)
returns table(lo bigint, hi bigint)
external name HTMIndex.[HTMIndex.HTMIndex].fHtmExtend
go

-- truncate a range of trixels to a lower depth (can return multiple rows)
create function dbo.fHtmTruncateRange(@lo bigint, @hi bigint, @level smallint)
returns table(htmid bigint)
external name HTMIndex.[HTMIndex.HTMIndex].fHtmTruncateRange
go


-- run script up to this point
set noexec on
go


-- generate a HTM ID from a pair of coordinates (this is in the HTM library)
create function sph.fHtmEq(@ra float, @dec float)
returns bigint external name SphericalHtm.[Spherical.Htm.Sql].fHtmEq
go


/*
 4. Drop all objects
*/
drop function HTMIndexCreate
drop function HTMIndexCreate2
drop function GeomToHTMChull
drop function GeomFromTrixel
drop function GeomFromTrixelEps
drop function fHtmTruncateRange
drop function fHtmTruncate
drop function fHtmExtend
go

drop assembly HTMIndex
drop assembly SphericalHtm
drop assembly SphericalLib
drop assembly [Spherical.Quickhull]
go

