/*
 * HTMIndex.cs -- Create a HTM index for a geographic region.
 * 
 *  * Copyright 2014 Kondor Dániel <kondor.dani@gmail.com>
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


using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Spherical;
using Spherical.Htm;
using Spherical.Shape;
using Microsoft.SqlServer.Types;

using System.Collections;
using System.Data.SqlTypes;


namespace HTMIndex {
    /// <summary>
    /// Class implementing the HTM-based index creation and some auxilliary functions for an SqlGeography object.
    /// The method is described in the manuscript: 'Efficient classification of billions of points into complex geographic regions using hierarchical triangular mesh',
    /// by Dániel Kondor, László Dobos, István Csabai, András Bodor, Gábor Vattay, Tamás Budavári and Alexander S. Szalay.
    /// For usage samples, see the accompanying SQL scripts (HTMIndexDeploy.sql and HTMIndexSample.sql).
    /// Website: http://www.vo.elte.hu/htmpaper/
    /// 
    /// This project requires the Spherical Toolkit available at http://voservices.net/spherical
    /// </summary>
    public class HTMIndex {

        /// <summary>
        /// Very simple IGeographySink110 interface, stores the geography object in a list of Cartesian structures.
        /// Only polygons are supported, multiple sub-polygons / figures are not separated.
        /// Can be used for multiple geographies in succession, in this case all points are stored together.
        /// The points can be accessed via the List field.
        /// Note that the list is never cleared through the lifetime of an instance; to start a new list, a new
        /// instance must be created.
        /// 
        /// Note: this worked for the geograpy objects created from the GAdm database of regions / maps. It might not
        /// work for objects which use the more complex features available in the SQLGeography type.
        /// </summary>
        private class ListSink : IGeographySink110 {
            private List<Cartesian> list;
            public List<Cartesian> List {
                get { return list; }
            }

            public ListSink() {
                list = new List<Cartesian>();
            }

            public void BeginGeography(OpenGisGeographyType type) {
                //!! TODO: implement support for other geography types !!
                if (type != OpenGisGeographyType.Polygon) throw new NotImplementedException("ListSink: Geography type not implemented!\n");
            }

            public void EndGeography() {

            }

            public void BeginFigure(double latitude, double longitude, double? z, double? m) {
                list.Add(new Cartesian(longitude, latitude));
            }

            public void AddLine(double latitude, double longitude, double? z, double? m) {
                list.Add(new Cartesian(longitude, latitude));
            }

            public void EndFigure() {

            }

            public void SetSrid(int id) {
                //!! TODO: check that the ID is supported? !!
            }

            public void AddCircularArc(double x1, double y1, double? z1, double? m1, double x2, double y2, double? z2, double? m2) {
                //!! TODO: implement this !!
                throw new NotImplementedException();
            }
        }


        /// <summary>
        /// Iterate (recursively) over the sub-geographies in a specified SqlGeography instance, and
        /// store the list of points from each.
        /// </summary>
        /// <param name="geom">The geography to use.</param>
        /// <param name="sink">Store the points in sink.List</param>
        private static void GetListIterate(SqlGeography geom, ListSink sink) {
            var numgeo = geom.STNumGeometries().Value;

            if (numgeo > 1) {
                for (int i = 0; i < numgeo; i++) {
                    GetListIterate(geom.STGeometryN(i + 1), sink);  // indexed from 1!
                }
            }

            else {
                var type = geom.STGeometryType().Value;

                switch (type) {

                    case "Polygon":
                        geom.Populate(sink);
                        break;
                    case "LineString":
                        break;
                    case "Point":
                        break;
                    case "CircularString":
                    case "CompoundCurve":
                    case "CurvePolygon":
                    case "GeometryCollection":
                    case "MultiPoint":
                    case "MultiLineString":
                    case "MultiPolygon":
                    default:
                        throw new NotImplementedException("GeomToHTM: Geography type not implemented!\n");    // TODO
                }
            }
        }

        /// <summary>
        /// Convert an SqlGeography instance to a list of points (i.e. the points along its contour).
        /// Only supports geographies made up of polygons.
        /// </summary>
        /// <param name="geom"></param>
        /// <returns></returns>
        public static List<Cartesian> GeomToList(SqlGeography geom) {
            ListSink sink = new ListSink();
            GetListIterate(geom, sink);
            return sink.List;
        }

        /// <summary>
        /// specify the method to use for converting an SqlGeography instance to a Convex (GeomToConvex2 function)
        /// </summary>
        public enum ConvexMethod {
            /// <summary>
            /// Use the convex hull generator in the Spherical.Quickhull namespace.
            /// </summary>
            SphChull,
            /// <summary>
            /// Use the convex hull generator provided by the SqlGeography class, and convert the result
            /// to a Spherical.Convex.
            /// </summary>
            SqlChull,
            /// <summary>
            /// Create an enclosing circle and convert it to a Spherical.Convex
            /// </summary>
            SqlCircle
        };

        /// <summary>
        /// Create a Spherical.Convex from an SqlGeography instance by creating a convex hull of it.
        /// </summary>
        /// <param name="geom">Region to use.</param>
        /// <param name="method">Method to use.</param>
        /// <returns>A Spherical.Convex object enclosing the original region.</returns>
        public static Convex GeomToConvex2(SqlGeography geom, ConvexMethod method) {
            if (method == ConvexMethod.SphChull) {
                List<Cartesian> list = GeomToList(geom);
                Chull.Cherror ce = Chull.Cherror.Ok;
                Convex co = Chull.Make(list, out ce);
                if (ce != Chull.Cherror.Ok) {
                    throw new Exception("Error creating the CHull!\n");
                }
                return co;
            }

            if (method == ConvexMethod.SqlChull) {
                SqlGeography geom2 = geom.STConvexHull();
                List<Cartesian> list = GeomToList(geom2); //!! TODO: are the points given by this method in the correct order? !!
                Convex co = new Convex(list, PointOrder.Safe);
                return co;
            }

            if (method == ConvexMethod.SqlCircle) {
                SqlGeography geom2 = geom.EnvelopeCenter();
                double lat = (double)geom2.Lat;
                double lon = (double)geom2.Long;
                double r = (double)geom.EnvelopeAngle();
                Halfspace h1 = new Halfspace(lon, lat, 60.0 * r);
                Convex co = new Convex(h1);
                return co;
            }

            throw new NotImplementedException("GeomToConvex2(): unknown method!\n");
        }
     

        /// <summary>
        /// Cover a Spherival.Convex object with HTM cells.
        /// </summary>
        /// <param name="co">The Convex to cover.</param>
        /// <param name="level">Level of cells to use.</param>
        /// <returns>List of covering cells as lo--hi pairs of level 20 HTM IDs.</returns>
        public static List<Int64> ConvexCover(Convex co, Int16 level) {
            Region r1 = new Region(co, false);
            r1.Simplify();

            Cover cr = new Cover(r1);
            do {
                cr.Step();
            }
            while (cr.GetLevel < level);
            return cr.GetTrixels(Markup.Outer);
        }

        /// <summary>
        /// Create a convex hull covering an SqlGeography instance and cover it with HTM cells.
        /// Note that only geographies consisting of polygons are supported
        /// </summary>
        /// <param name="geom">The geography to cover.</param>
        /// <param name="level">The HTM level to use.</param>
        /// <param name="method">The method to use.</param>
        /// <returns>List of covering cells as lo--hi pairs of level 20 HTM IDs.</returns>
        public static List<Int64> GetList2(SqlGeography geom, Int16 level, ConvexMethod method) {
            Convex co = GeomToConvex2(geom, method);
            return ConvexCover(co, level);
        }

        /// <summary>
        /// Create a convex hull covering an SqlGeography instance using the generator in the Spherical.Quickhull
        /// namespace and cover it with HTM cells.
        /// Note that only geographies consisting of polygons are supported
        /// </summary>
        /// <param name="geom">The geography to cover.</param>
        /// <param name="level">The HTM level to use.</param>
        /// <returns>List of covering cells as lo--hi pairs of level 20 HTM IDs.</returns>
        [Microsoft.SqlServer.Server.SqlFunction(
            FillRowMethodName = "CoverFill",
            TableDefinition = "Htm20Start bigint, Htm20End bigint")]
        public static IEnumerable GeomToHTMChull(SqlGeography geom, SqlInt16 level) {
            return GetList2(geom, (Int16)level, ConvexMethod.SqlChull);
        }

        /// <summary>
        /// Fill method for the GeomToHTMChull() table-valued function
        /// </summary>
        /// <param name="pair1"></param>
        /// <param name="lo"></param>
        /// <param name="hi"></param>
        public static void CoverFill(Object pair1, out SqlInt64 lo, out SqlInt64 hi) {
            Int64Pair pair = (Int64Pair)pair1;
            lo = pair.lo;
            hi = pair.hi;
        }


        /// <summary>
        /// Create an SqlGeography representing a single HTM cell.
        /// </summary>
        /// <param name="trixel">The HTM cell.</param>
        /// <returns>An SqlGeography </returns>
        [Microsoft.SqlServer.Server.SqlFunction]
        public static SqlGeography GeomFromTrixel(Int64 trixel) {
            Cartesian a, b, c;
            Trixel.ToTriangle(trixel, out a, out b, out c);
            SqlGeographyBuilder builder = new SqlGeographyBuilder();
            builder.SetSrid(4326);
            builder.BeginGeography(OpenGisGeographyType.Polygon);
            builder.BeginFigure(a.Dec, a.RA);
            builder.AddLine(b.Dec, b.RA);
            builder.AddLine(c.Dec, c.RA);
            builder.AddLine(a.Dec, a.RA);
            builder.EndFigure();
            builder.EndGeography();
            return builder.ConstructedGeography;
        }

        /// <summary>
        /// Create an SqlGeography representing a single HTM cell, shrunk by the given factor.
        /// </summary>
        /// <param name="trixel">The HTM cell.</param>
        /// <param name="eps">Shrinking factor. The coordinates for the triangle are given by:
        ///     v_i = c + (1-eps)*(v_i - c), where v_i are the original coordinates and c is the
        ///     center point.</param>
        /// <returns>An SqlGeography </returns>
        [Microsoft.SqlServer.Server.SqlFunction]
        public static SqlGeography GeomFromTrixelEps(Int64 trixel, SqlDouble eps) {
            Cartesian[] v = new Cartesian[3];
            double eps2 = (double)eps;
            Trixel.ToTriangle(trixel, out v[0], out v[1], out v[2]);
            Cartesian m = Cartesian.CenterOfMass(v, false);
            for (int i = 0; i < 3; i++) {
                v[i].X -= eps2 * (v[i].X - m.X);
                v[i].Y -= eps2 * (v[i].Y - m.Y);
                v[i].Z -= eps2 * (v[i].Z - m.Z);
                v[i].Normalize();
            }
            double lat1 = v[0].Dec;
            double lon1 = v[0].RA;
            SqlGeographyBuilder builder = new SqlGeographyBuilder();
            builder.SetSrid(4326);
            builder.BeginGeography(OpenGisGeographyType.Polygon);
            builder.BeginFigure(lat1, lon1);
            double lat2 = v[1].Dec;
            double lon2 = v[1].RA;
            builder.AddLine(lat2, lon2);
            lat2 = v[2].Dec;
            lon2 = v[2].RA;
            builder.AddLine(lat2, lon2);
            builder.AddLine(lat1, lon1);
            builder.EndFigure();
            builder.EndGeography();
            return builder.ConstructedGeography;
        }


        public static IEnumerable<Int64> Int64Range(Int64 lo, Int64 hi) {
            for (Int64 i = lo; i <= hi; i++) yield return i;
        }


        /// <summary>
        /// Structure for storing an HTM ID and its state and an associated geography object together.
        /// </summary>
        public struct Int64Geom : IComparable<Int64Geom>, IEquatable<Int64Geom> {
            public Int64 id;
            public Markup state;
            public SqlGeography geom;
            public int CompareTo(Int64Geom o) {
                return this.id.CompareTo(o.id);
            }
            public bool Equals(Int64Geom o) {
                return this.id.Equals(o.id);
            }
            public override int GetHashCode() {
                return this.id.GetHashCode();
            }
        }


        /// <summary>
        /// Evaluate the given list of trixels if they are contained in a specified region. Evaluates partial trixels
        /// by recursively iterating over the intersection. Also return the intersection of the region with the partial trixels.
        /// This corresponds to Algorithm 1 in the manuscript.
        /// </summary>
        /// <param name="geom">The region to use.</param>
        /// <param name="trixels">List of trixels to evaluate.</param>
        /// <param name="level">Maxium depth</param>
        /// <param name="levelskip">Increase depth by this amount in each iteration (1,2, maybe 3?)</param>
        /// <param name="shrinkeps">Shrink trixels by 1-shrinkeps before testing for containment to avoid numerical errors. Highly recommended
        ///     to set this to some > 0 value (e.g. 1e-10 worked fine for me); due to limited numerical precision, the STContains() function
        ///     sometimes gives false negative results (e.g. it returns 0, for trixels 14248 and 227971, while in reality, trixels 14248 does
        ///     contain 227971 (227971 / 16 = 14248).</param>
        /// <param name="partialint">If true, also return an SqlGeography object for the intersection of each partial trixel with the
        ///     region; these could be used for optimized classification of points in partial trixels.</param>
        /// <returns>List of trixels completely or partially contained in the region.</returns>
        public static IEnumerable EvalTrixels3(SqlGeography geom, IEnumerable<Int64> trixels, Int16 level, Int16 levelskip,
                double shrinkeps, bool partialint) {
            bool shrink = (shrinkeps > 0.0);
            foreach (Int64 t in trixels) {
                SqlGeography t2 = null;
                if (shrink) t2 = GeomFromTrixelEps(t, shrinkeps); //shrink by a small factor to avoid incorrect classification due to
                        //round-off errors
                else t2 = GeomFromTrixel(t);
                bool contains = false;
                contains = (bool)geom.STContains(t2);
                if (contains) { // full trixel
                    Int64Geom t3;
                    t3.id = t;
                    t3.state = Markup.Inner;
                    t3.geom = null;
                    yield return t3;
                    continue;
                }

                //partial or disjuct trixel
                if (shrink) t2 = GeomFromTrixel(t); //we calculate the intersection with the original trixel
                SqlGeography g2 = geom.STIntersection(t2);
                //!! TODO: I'm not sure what this will return if the two regions are disjoint. One of the following two lines works fine. !!
                if (g2 == null) continue;
                if (g2.STIsEmpty()) continue;

                //partial trixel
                Int16 l2 = (Int16)Trixel.LevelOfHid(t);
                if (l2 >= level) {
                    //already at maximum level, add to the return list
                    Int64Geom t3;
                    t3.id = t;
                    t3.state = Markup.Partial;
                    if (partialint) t3.geom = g2; //store the intersection too, if requested
                    else t3.geom = null;
                    yield return t3;
                    continue;
                }

                //continue the recursion
                int level2 = l2 + levelskip;
                if (level2 > level) level2 = level;
                Int64Pair t4 = Trixel.Extend(t, level2);
                //!! TODO: using nested (recursive) iterators may not be ideal; probably still better than adding everything to a return list, this
                //      way, the caller can start storing the results before execution completes
                foreach (Int64Geom g in EvalTrixels3(g2, Int64Range(t4.lo, t4.hi), level, levelskip, shrinkeps, partialint)) { //hi is included in the range
                    yield return g;
                }
            }
        }


        /// <summary>
        /// Cover a region (an instance of SqlGeography) with HTM cells on a given maximum depth. Use the (hopefully)
        /// faster version of the trixel evaluator in which at deeper levels only the intersection of the region with
        /// the partial trixels is evaluated. Return also these intersections for partial trixels if requested.
        /// </summary>
        /// <param name="geom">region to test</param>
        /// <param name="level">maximum HTM depth in the results</param>
        /// <param name="chulllevel">starting HTM level (generate a convex hull on this level to start the iteration with)</param>
        /// <param name="partialint">If true, also return an SqlGeography object for the intersection of each partial trixel with the
        ///     region; these could be used for optimized classification of points in partial trixels.</param>
        /// <param name="shrinkeps">Shrink trixels by 1-shrinkeps before testing for containment to avoid numerical errors. Highly recommended
        ///     to set this to some > 0 value (e.g. 1e-10 worked fine for me); due to limited numerical precision, the STContains() function
        ///     sometimes gives false negative results (e.g. it returns 0, for trixels 14248 and 227971, while in reality, trixels 14248 does
        ///     contain 227971 (227971 / 16 = 14248).</param>
        /// <returns>List of trixels giving a covering of the region.</returns>
        [Microsoft.SqlServer.Server.SqlFunction(
            FillRowMethodName = "TrixelFillGeom",
            TableDefinition = "lo bigint, hi bigint, full bit, geomint geography")]
        public static IEnumerable HTMIndexCreate2(SqlGeography geom, SqlInt16 level, SqlDouble shrinkeps, SqlInt16 chulllevel,
                SqlBoolean partialint) {
            double shrink = (double)shrinkeps;
            short chlevel = (short)chulllevel;
            if (chlevel == 0 || chlevel > 16) chlevel = 10;
            if (shrink < 0.0) throw new ArgumentException("GeomToHTM2Eps(): shrink parameter must be >= 0!\n");
            List<Int64> trixels1 = GetList2(geom, chlevel, ConvexMethod.SphChull);
            return EvalTrixels3(geom, trixels1, (Int16)level, 2, shrink, (bool)partialint);
        }

        /// <summary>
        /// Cover a region (an instance of SqlGeography) with HTM cells on a given maximum depth, using the default
        /// (recommended) parameters. Use the (hopefully) faster version of the trixel evaluator in which at deeper levels
        /// only the intersection of the region with the partial trixels is evaluated. Return also these intersections
        /// for partial trixels if requested.
        /// </summary>
        /// <param name="geom">region to test</param>
        /// <param name="level">maximum HTM depth in the results</param>
        /// <param name="partialint">If true, also return an SqlGeography object for the intersection of each partial trixel with the
        ///     region; these could be used for optimized classification of points in partial trixels.</param>
        /// <returns>List of trixels giving a covering of the region.</returns>
        [Microsoft.SqlServer.Server.SqlFunction(
            FillRowMethodName = "TrixelFillGeom",
            TableDefinition = "lo bigint, hi bigint, full bit, geomint geography")]
        public static IEnumerable HTMIndexCreate(SqlGeography geom, SqlInt16 level, SqlBoolean partialint) {
            return HTMIndexCreate2(geom, level, 1e-10, 8, partialint);
        }

        /// <summary>
        /// Row fill function for the GeomToHTM3Eps table-valued function.
        /// </summary>
        /// <param name="t1"></param>
        /// <param name="lo"></param>
        /// <param name="hi"></param>
        /// <param name="full"></param>
        /// <param name="geomint"></param>
        public static void TrixelFillGeom(Object t1, out SqlInt64 lo, out SqlInt64 hi, out SqlBoolean full, out SqlGeography geomint) {
            Int64Geom trixel = (Int64Geom)t1;
            Int64Pair pair = Trixel.Extend(trixel.id, 20);
            lo = pair.lo;
            hi = pair.hi;
            if (trixel.state == Markup.Inner) full = true;
            else full = false;
            geomint = trixel.geom;
        }


        /// <summary>
        /// Helper function: truncate a trixel to a given HTM level.
        /// </summary>
        /// <param name="htmid">Trixel to truncate.</param>
        /// <param name="level">Level to truncate to.</param>
        /// <returns>Truncated trixel ID.</returns>
        [Microsoft.SqlServer.Server.SqlFunction(IsDeterministic = true, IsPrecise = true)]
        public static SqlInt64 fHtmTruncate(SqlInt64 htmid, SqlInt16 level) {
            return (SqlInt64)Trixel.Truncate((Int64)htmid, (int)level);
        }

        public static IEnumerable<Int64Pair> Int64PairIE(Int64Pair r) {
            yield return r;
        }

        /// <summary>
        /// Extend a trixel to the given level.
        /// </summary>
        /// <param name="htmid">Trixel to extend.</param>
        /// <param name="level">Level to extend to.</param>
        /// <returns>The resulting range of trixels.</returns>
        [Microsoft.SqlServer.Server.SqlFunction(IsDeterministic = true, IsPrecise = true, FillRowMethodName = "PairFill", TableDefinition = "lo bigint, hi bigint")]
        public static IEnumerable fHtmExtend(SqlInt64 htmid, SqlInt16 level) {
            Int64Pair r = Trixel.Extend((Int64)htmid, (int)level);
            return Int64PairIE(r);
        }

        /// <summary>
        /// Fill row function for fHtmExtend().
        /// </summary>
        /// <param name="pair1"></param>
        /// <param name="lo"></param>
        /// <param name="hi"></param>
        public static void PairFill(Object pair1, out SqlInt64 lo, out SqlInt64 hi) {
            Int64Pair pair = (Int64Pair)pair1;
            lo = pair.lo;
            hi = pair.hi;
        }

        /// <summary>
        /// Fill row function for fHtmTrucateRange()
        /// </summary>
        /// <param name="hid"></param>
        /// <param name="htm"></param>
        public static void SingleFill(Object hid, out SqlInt64 htm) {
            htm = (Int64)hid;
        }

        /// <summary>
        /// Truncate a range of trixels into a lower level range (i.e. return a list of trixels of the given level which contain all
        /// of the trixels in the original range).
        /// </summary>
        /// <param name="lo">Start of the range to be truncated.</param>
        /// <param name="hi">End of the range to be truncated.</param>
        /// <param name="level">Level to truncate to.</param>
        /// <returns>List of truncated trixels.</returns>
        public static IEnumerable<Int64> TruncateRange(Int64 lo, Int64 hi, int level) {
            int l2 = Trixel.LevelOfHid(lo);
            int l3 = Trixel.LevelOfHid(hi);
            if (l2 != l3) throw new ArgumentException("GeomToHTM.TruncateRange(): invalid arguments!\n");
            if (hi < lo) throw new ArgumentException("GeomToHTM.TruncateRange(): invalid arguments!\n");
            Int64 t1 = Trixel.Truncate(lo, level);
            Int64 t2 = Trixel.Truncate(hi, level);
            for (Int64 i = t1; i <= t2; i++) yield return i;
        }

        /// <summary>
        /// Truncate a range of trixels into a lower level range (i.e. return a list of trixels of the given level which contain all
        /// of the trixels in the original range).
        /// </summary>
        /// <param name="lo">Start of the range to be truncated.</param>
        /// <param name="hi">End of the range to be truncated.</param>
        /// <param name="level">Level to truncate to.</param>
        /// <returns>List of truncated trixels.</returns>
        [Microsoft.SqlServer.Server.SqlFunction(IsDeterministic = true, IsPrecise = true, FillRowMethodName = "SingleFill", TableDefinition = "htmid bigint")]
        public static IEnumerable fHtmTruncateRange(SqlInt64 lo, SqlInt64 hi, SqlInt16 level) {
            return TruncateRange((Int64)lo, (Int64)hi, (int)level);
        }
    }
}

