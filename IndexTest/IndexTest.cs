/*
 * IndexTest.cs -- Create a HTM index for a geographic region, console test program.
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


/*
 * This program computes the covering of a region given as an SqlGeography object and outputs
 * the results to its standard output. It expects the region as a result of an SQL query run
 * on an instance of Microsoft SQL Server 2012.
 * usage:
 *      IndexTest.exe [options]
 * possible options:
 *      -c -- connection string to use, set to something appropriate for your setup
 *      -q -- query to run, the first column should return an SqlGeography object to
 *          process (while the program will process any number of rows, it does not
 *          write any further identifiers on its output, so it does not make much sense
 *          to run it with multiple regions)
 *      -l -- depth of the covering to generate (valid values are in the range of 1--20,
 *          values around 12--15 are recommended for covering countries)
 *      -L -- levels to skip in one recursive call (cells are split into 4^L parts in each
 *          iteration), default is 2, recommended values 1--3
 *      -t -- if this option is present, only the starting convex hull is generated and 
 *          output as HTM ID ranges, the actual algorithm is not run
 *      -C -- file to write out the generated convex hull (in the format of the Spherical
 *          Library, mainly for debug purposes)
 *      -s -- generate a covering circle as a starting set for the algorithm (instead of a
 *          convex hull)
 *      -S -- use the convex hull generator in the SQL Server geographic library for
 *          generating the starting set
 *      -f -- use the full globe (all level 0 trixels) as a starting set
 *      -e -- shrink factor for containment tests (default: 1e-10, this seems to work fine)
 *      -d -- level to start the recursion at (generate the starting set at this level)
 *      
 * Example:
 *      IndexTest.exe -q 'select Geom from GAdm.dbo.Region where ID = 3410' -l 13 -d 7 > test3410.out
 */


using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.Types;
using HTMIndex;
using System.Data.SqlTypes;
using System.Data;
using System.Data.SqlClient;
using Spherical;
using Spherical.Htm;

namespace Test1 {
    class Program {
        static void Main(string[] args) {
            string query = null;
            string cstr = "Server=FUTURE1;Integrated Security=True;Type System Version=SQL Server 2012;"; //!! TODO: change this to whatever appropriate !!
            SqlInt16 level = 12;
            SqlInt16 coverlevel = 8;
            SqlInt16 levelskip = 2;
            double eps = 1e-10;
            bool testcover = false;
            string convexout = null;
            HTMIndex.HTMIndex.ConvexMethod method = HTMIndex.HTMIndex.ConvexMethod.SphChull;
            bool fullglobe = false;
            bool sqltest = true;
            for (uint i = 0; i < args.Length; i++) if (args[i][0] == '-') switch (args[i][1]) {
                        case 'q':
                            query = args[i + 1];
                            break;
                        case 'c':
                            cstr = args[i + 1];
                            break;
                        case 'l':
                            level = Convert.ToInt16(args[i + 1]);
                            break;
                        case 'L':
                            levelskip = Convert.ToInt16(args[i + 1]);
                            sqltest = false;
                            break;
                        case 't':
                            testcover = true;
                            break;
                        case 'C':
                            convexout = args[i + 1];
                            break;
                        case 'd':
                            coverlevel = Convert.ToInt16(args[i + 1]);
                            break;
                        case 's':
                            method = HTMIndex.HTMIndex.ConvexMethod.SqlCircle;
                            break;
                        case 'S':
                            method = HTMIndex.HTMIndex.ConvexMethod.SqlChull;
                            break;
                        case 'f':
                            fullglobe = true;
                            break;
                        case 'e':
                            eps = Convert.ToDouble(args[i + 1]);
                            break;
                    }
            StreamWriter cout = null;
            if (convexout != null) cout = new StreamWriter(convexout);
            SqlConnection sccn = new SqlConnection(cstr);
            sccn.Open();
            SqlCommand cmd = new SqlCommand(query, sccn);
            SqlDataReader d = cmd.ExecuteReader();
            while (d.Read()) {
                SqlGeography geom = (SqlGeography)d[0];

                Convex co1 = null;
                if (!sqltest) co1 = HTMIndex.HTMIndex.GeomToConvex2(geom, method);
                if (cout != null) cout.Write(co1);

                List<Int64> trixels1 = null;
                if (!sqltest) {
                    if (fullglobe) {
                        trixels1 = new List<Int64>(8);
                        for (int i = 0; i < 8; i++) trixels1.Add((Int64)(i + 8));
                    }
                    else trixels1 = HTMIndex.HTMIndex.ConvexCover(co1, (Int16)level);
                }

                if (testcover) {
                    foreach (Int64 t in trixels1) Console.WriteLine(t);
                }
                else {
                    IEnumerable res = null;
                    if (!sqltest) res = HTMIndex.HTMIndex.EvalTrixels3(geom, trixels1, (Int16)level, (Int16)levelskip, eps, false);
                    else res = HTMIndex.HTMIndex.HTMIndexCreate2(geom, level, eps, coverlevel, false);

                    SqlInt64 lo;
                    SqlInt64 hi;
                    SqlBoolean full;
                    SqlGeography g1;
                    foreach (Object o in res) {
                        HTMIndex.HTMIndex.TrixelFillGeom(o, out lo, out hi, out full, out g1);
                        Int64 lo2 = (Int64)lo;
                        Int64 hi2 = (Int64)hi;
                        Int32 full2 = 0;
                        if (full) full2 = 1;
                        Console.WriteLine("{0}\t{1}\t{2}", lo2, hi2, full2);
                    }
                }
            }
            if (cout != null) cout.Close();
            Console.Write("\n");

        }
    }
}


