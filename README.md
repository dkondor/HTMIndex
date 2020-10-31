# HTMIndex

Code to cover a geographical region with triangles from a [Hierarchical Triangular Mesh](http://www.skyserver.org/htm/) (HTM trixels) that can be used for geographical indexing. This repository contains the original, single-theaded code for our [paper](https://doi.org/10.1145/2618243.2618245):
```
Kondor, D., Dobos, L., Csabai, I., Bodor, A., Vattay, G., Budavári, T., & Szalay, A. S. (2014).
Efficient classification of billions of points into complex geographic regions using hierarchical triangular mesh.
Proceedings of the 26th International Conference on Scientific and Statistical Database Management - SSDBM ’14. DOI:10.1145/2618243.2618245
```
An improved, multi-threaded implementation by [László Dobos](https://github.com/dobos) is available at https://github.com/eltevo/twgeo.

## Requirements

This code is written for the specific use with Microsoft SQL Server. It expects to receive the geography object to use from the database server, and is optimized to generate a database table as results.

There are the following dependencies:
 - `Microsoft.SqlServer.Types.dll` -- this library is supplied with SQL Server 2012 (and newer) and it handles calculating the intersection of geographical regions in our case
 - Spherical library, available at http://voservices.net/spherical/Downloads.aspx (all three downloadable DLLs), provides functionality for dealing with HTM trixels.

Code in this repository was developed and tested with Visual Studio 2010 and SQL Server 2012.

Functionality is documented mainly in the [source](HTMIndex/HTMIndex.cs). Also see the SQL files for info on installation and example usage.

