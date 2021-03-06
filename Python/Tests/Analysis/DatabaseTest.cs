// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Interpreter.LegacyDB;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;
using TestUtilities.Mocks;
using TestUtilities.Python;

namespace AnalysisTests {
    [TestClass]
    public class DatabaseTest {
        [ClassInitialize]
        public static void DoDeployment(TestContext context) {
            AssertListener.Initialize();
        }

        [TestMethod, Priority(0)]
        public void Invalid2xDatabase() {
            using (var db = MockCompletionDB.Create(PythonLanguageVersion.V27,
                // __bad_builtin__ is missing str
                Tuple.Create("__bad_builtin__", "__builtin__")
                )) {
                var ptd = db.Database;

                Assert.IsNotNull(ptd.GetModule("__builtin__"));

                var analyzer = db.MakeAnalyzer();

                // String type should have been loaded from the default DB.
                Assert.IsNotNull(analyzer.ClassInfos[BuiltinTypeId.Str]);
                Assert.IsNotNull(analyzer.ClassInfos[BuiltinTypeId.Bytes]);
            }
        }

        [TestMethod, Priority(0)]
        public void Invalid3xDatabase() {
            using (var db = MockCompletionDB.Create(PythonLanguageVersion.V33,
                // bad_builtins is missing str
                Tuple.Create("bad_builtins", "builtins")
                )) {
                var ptd = db.Database;

                Assert.IsNotNull(ptd.GetModule("builtins"));

                var analyzer = db.MakeAnalyzer();

                // String type should have been loaded from the default DB.
                Assert.IsNotNull(analyzer.ClassInfos[BuiltinTypeId.Str]);
                // String type is the same as unicode, but not bytes.
                Assert.AreNotEqual(analyzer.Types[BuiltinTypeId.Bytes], analyzer.Types[BuiltinTypeId.Str]);
                Assert.AreEqual(analyzer.Types[BuiltinTypeId.Unicode], analyzer.Types[BuiltinTypeId.Str]);
                // String's module name is 'builtins' and not '__builtin__'
                // because we replace the module name based on the version
                // despite using the 2.7-based database.
                Assert.AreEqual("builtins", analyzer.Types[BuiltinTypeId.Str].DeclaringModule.Name);
            }
        }

        [TestMethod, Priority(0)]
        public void LayeredDatabase() {
            using (var db1 = MockCompletionDB.Create(PythonLanguageVersion.V27, "os"))
            using (var db2 = MockCompletionDB.Create(PythonLanguageVersion.V27, "posixpath")) {
                Assert.IsNotNull(db1.Database.GetModule("os"));
                Assert.IsNull(db1.Database.GetModule("posixpath"));

                var ptd1 = db1.Database;
                var ptd2 = ptd1.Clone();
                ptd2.LoadDatabase(db2.DBPath);

                Assert.IsNull(ptd1.GetModule("posixpath"));
                Assert.IsNotNull(ptd2.GetModule("os"));
                Assert.IsNotNull(ptd2.GetModule("posixpath"));
                Assert.AreSame(ptd1.GetModule("os"), db1.Database.GetModule("os"));
                Assert.AreSame(ptd2.GetModule("os"), db1.Database.GetModule("os"));
            }

            var factory = PythonInterpreterFactoryWithDatabase.CreateFromDatabase(new Version(2, 7));

            using (var db1 = MockCompletionDB.Create(PythonLanguageVersion.V27, "os", "posixpath"))
            using (var db2 = MockCompletionDB.Create(PythonLanguageVersion.V27, "posixpath")) {
                var ptd1 = new PythonTypeDatabase(factory, new[] {
                    db1.DBPath,
                    db2.DBPath
                });

                Assert.IsNotNull(ptd1.GetModule("posixpath"));
                Assert.AreNotSame(ptd1.GetModule("posixpath"), db1.Database.GetModule("posixpath"));
                Assert.AreNotSame(ptd1.GetModule("posixpath"), db2.Database.GetModule("posixpath"));

                var ptd2 = new PythonTypeDatabase(factory, new[] {
                    db2.DBPath,
                    db1.DBPath
                });

                Assert.IsNotNull(ptd2.GetModule("posixpath"));
                Assert.AreNotSame(ptd2.GetModule("posixpath"), db1.Database.GetModule("posixpath"));
                Assert.AreNotSame(ptd2.GetModule("posixpath"), db2.Database.GetModule("posixpath"));
            }

            using (var db1 = MockCompletionDB.Create(PythonLanguageVersion.V27, "os", "posixpath"))
            using (var db2 = MockCompletionDB.Create(PythonLanguageVersion.V27, "posixpath"))
            using (var db3 = MockCompletionDB.Create(PythonLanguageVersion.V27, "ntpath")) {
                var ptd = db1.Database;
                Assert.AreSame(ptd.GetModule("posixpath"), db1.Database.GetModule("posixpath"));
                Assert.AreNotSame(ptd.GetModule("posixpath"), db2.Database.GetModule("posixpath"));

                var ptd2 = ptd.Clone();
                ptd2.LoadDatabase(db2.DBPath);

                Assert.AreNotSame(ptd2.GetModule("posixpath"), ptd.GetModule("posixpath"));

                var ptd3 = ptd2.Clone();
                ptd3.LoadDatabase(db3.DBPath);

                Assert.IsNotNull(ptd3.GetModule("ntpath"));
                Assert.IsNull(ptd2.GetModule("ntpath"));
            }
        }

        [TestMethod, Priority(0)]
        public void PropertyOfUnknownType() {
            using (var db = MockCompletionDB.Create(PythonLanguageVersion.V34, "property_of_unknown_type")) {
                var ptd = db.Database;
                var module = ptd.GetModule("property_of_unknown_type");
                Assert.IsNotNull(module);
                var cls = module.GetMember(null, "Class");
                Assert.IsInstanceOfType(cls, typeof(IPythonType));
                var propObj = ((IPythonType)cls).GetMember(null, "no_return");
                Assert.IsInstanceOfType(propObj, typeof(IBuiltinProperty));
                var prop = (IBuiltinProperty)propObj;

                // The type is unspecified in the DB, so it should be object
                Assert.IsNotNull(prop.Type, "Property type should never be null");
                Assert.AreEqual(BuiltinTypeId.Object, prop.Type.TypeId, "Property should be of type object");
                Assert.AreEqual("property of type object", prop.Description);

                // Ensure that we are still getting properties at all
                propObj = ((IPythonType)cls).GetMember(null, "with_return");
                Assert.IsInstanceOfType(propObj, typeof(IBuiltinProperty));
                prop = (IBuiltinProperty)propObj;

                Assert.IsNotNull(prop.Type, "Property should not have null type");
                Assert.AreEqual("property of type int", prop.Description);
            }
        }
    }

    [TestClass]
    public class DatabaseTest27 {
        static DatabaseTest27() {
            AssertListener.Initialize();
        }

        public virtual PythonVersion Python {
            get { return PythonPaths.Python27 ?? PythonPaths.Python27_x64; }
        }

        [TestMethod, Priority(0)]
        public async Task GetSearchPaths() {
            Python.AssertInstalled();

            var paths = await PythonLibraryPath.GetUncachedDatabaseSearchPathsAsync(Python.InterpreterPath);
            Console.WriteLine("Paths for {0}", Python.InterpreterPath);
            foreach (var path in paths) {
                Console.WriteLine("{0} {1}", path.Path, path.IsStandardLibrary ? "(stdlib)" : "");
            }

            // Python.PrefixPath and LibraryPath should be included.
            // We can't assume anything else
            AssertUtil.ContainsAtLeast(paths.Select(p => p.Path.ToLowerInvariant().TrimEnd('\\')),
                Python.PrefixPath.ToLowerInvariant().TrimEnd('\\'),
                Path.Combine(Python.PrefixPath, "Lib").ToLowerInvariant().TrimEnd('\\')
            );
            
            // All paths should exist
            AssertUtil.ArrayEquals(paths.Where(p => !Directory.Exists(p.Path)).ToList(), new PythonLibraryPath[0]);

            // Ensure we can round-trip the entries via ToString/Parse
            var asStrings = paths.Select(p => p.ToString()).ToList();
            var asPaths = asStrings.Select(PythonLibraryPath.Parse).ToList();
            var asStrings2 = asPaths.Select(p => p.ToString()).ToList();
            AssertUtil.ArrayEquals(asStrings, asStrings2);
            AssertUtil.ArrayEquals(paths, asPaths, (o1, o2) => {
                PythonLibraryPath p1 = (PythonLibraryPath)o1, p2 = (PythonLibraryPath)o2;
                return p1.Path == p2.Path && p1.IsStandardLibrary == p2.IsStandardLibrary;
            });

            var dbPath = Path.Combine(TestData.GetTempPath(), "database.path");
            Assert.IsNull(PythonLibraryPath.GetCachedDatabaseSearchPaths(dbPath),
                "Should not have found cached paths in an empty directory");

            PythonLibraryPath.WriteDatabaseSearchPaths(dbPath, paths);
            Assert.IsTrue(File.Exists(dbPath));
            var paths2 = PythonLibraryPath.GetCachedDatabaseSearchPaths(dbPath);
            AssertUtil.ArrayEquals(paths, paths2, (o1, o2) => {
                PythonLibraryPath p1 = (PythonLibraryPath)o1, p2 = (PythonLibraryPath)o2;
                return p1.Path == p2.Path && p1.IsStandardLibrary == p2.IsStandardLibrary;
            });
        }

        [TestMethod, Priority(0)]
        public async Task GetExpectedDatabaseModules() {
            Python.AssertInstalled();

            var db = PythonTypeDatabase.GetDatabaseExpectedModules(
                Python.Version.ToVersion(),
                await PythonLibraryPath.GetUncachedDatabaseSearchPathsAsync(Python.InterpreterPath)
            ).ToList();

            var stdlib = db[0];
            AssertUtil.ContainsAtLeast(stdlib.Select(mp => mp.FullName),
                "os", "ctypes.__init__", "encodings.utf_8", "ntpath"
            );

        }
    }

    [TestClass]
    public class DatabaseTest26 : DatabaseTest27 {
        public override PythonVersion Python {
            get { return PythonPaths.Python26 ?? PythonPaths.Python26_x64; }
        }
    }

    [TestClass]
    public class DatabaseTest31 : DatabaseTest27 {
        public override PythonVersion Python {
            get { return PythonPaths.Python31 ?? PythonPaths.Python31_x64; }
        }
    }

    [TestClass]
    public class DatabaseTest32 : DatabaseTest27 {
        public override PythonVersion Python {
            get { return PythonPaths.Python32 ?? PythonPaths.Python32_x64; }
        }
    }

    [TestClass]
    public class DatabaseTest33 : DatabaseTest27 {
        public override PythonVersion Python {
            get { return PythonPaths.Python33 ?? PythonPaths.Python33_x64; }
        }
    }

    [TestClass]
    public class DatabaseTest34 : DatabaseTest27 {
        public override PythonVersion Python {
            get { return PythonPaths.Python34 ?? PythonPaths.Python34_x64; }
        }
    }

    [TestClass]
    public class DatabaseTest35 : DatabaseTest27 {
        public override PythonVersion Python {
            get { return PythonPaths.Python35 ?? PythonPaths.Python35_x64; }
        }
    }

    [TestClass]
    public class DatabaseTestIPy27 : DatabaseTest27 {
        public override PythonVersion Python {
            get { return PythonPaths.IronPython27; }
        }
    }

    [TestClass]
    public class DatabaseTestIPy27x64 : DatabaseTest27 {
        public override PythonVersion Python {
            get { return PythonPaths.IronPython27_x64; }
        }
    }

}
