using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace TestRunner
{
    public class Tests
    {
        public async Task<byte[]> CompileProject(string prjPath)
        {
            var ws = MSBuildWorkspace.Create();
            var prj = await ws.OpenProjectAsync(prjPath);
            var compilation = await prj.GetCompilationAsync();
            using (var pe = new MemoryStream())
            {
                var result = compilation.Emit(pe);
                if (false == result.Success)
                    throw new Exception("Compilation failed! " + String.Join(", ", result.Diagnostics));
                return pe.ToArray();
            }
        }

        private static string GetCurrentDir([CallerFilePath] string filename = null)
        {
            return Path.GetDirectoryName(filename);
        }

        [InlineData("1\\TestAssembly\\TestAssembly.csproj", "1\\TestAssembly\\TestAssembly.csproj")]
        [InlineData("2\\TestAssembly\\TestAssembly.csproj", "2\\TestAssembly\\TestAssembly.csproj")]
        [InlineData("3\\TestAssembly\\TestAssembly.csproj", "3\\TestAssembly\\TestAssembly.csproj")]

        [InlineData("1\\TestAssembly\\TestAssembly.csproj", "2\\TestAssembly\\TestAssembly.csproj")]
        [InlineData("2\\TestAssembly\\TestAssembly.csproj", "3\\TestAssembly\\TestAssembly.csproj")]
        [InlineData("1\\TestAssembly\\TestAssembly.csproj", "3\\TestAssembly\\TestAssembly.csproj")]

        [Theory]
        public async Task CompareShouldBeEqual(string prj1, string prj2)
        {
            var baseDir = Path.Combine(GetCurrentDir(), "..", "TestAssemblies");
            var prj1Bin = await CompileProject(Path.Combine(baseDir, prj1));
            var prj2Bin = await CompileProject(Path.Combine(baseDir, prj2));

            using (var msIn = new MemoryStream(prj1Bin))
            using (var msOut = new MemoryStream())
            {
                Hydra.ReferenceAssembly.createRefAsm(msIn, msOut);
                prj1Bin = msOut.ToArray();
            }

            using (var msIn = new MemoryStream(prj2Bin))
            using (var msOut = new MemoryStream())
            {
                Hydra.ReferenceAssembly.createRefAsm(msIn, msOut);
                prj2Bin = msOut.ToArray();
            }

            var areEqual = prj1Bin.SequenceEqual(prj2Bin);

            if (false == areEqual)
            {
                var comparer = new KellermanSoftware.CompareNetObjects.CompareObjects();

                var guid = Guid.NewGuid();
                File.WriteAllBytes($"failed_test_1_{guid}", prj1Bin);
                File.WriteAllBytes($"failed_test_2_{guid}", prj2Bin);
                
                comparer.MaxDifferences = 100;
                comparer.Compare(
                    AssemblyDefinition.ReadAssembly(new MemoryStream(prj1Bin)),
                    AssemblyDefinition.ReadAssembly(new MemoryStream(prj2Bin)));
                var diffString = comparer.DifferencesString;
                Assert.True(areEqual, $"Test failed for {prj1} -> {prj2}: {diffString}");
            }
        }
    }
}
