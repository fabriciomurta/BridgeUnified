﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

namespace Bridge.Translator.Tests
{
    [TestFixture]
    class OutputTest
    {
        private const string LogFileNameWithoutExtention = "testProjectsBuild";
        private const string BuildArguments = "/flp:Verbosity=diagnostic;LogFile=" + LogFileNameWithoutExtention + ".log;Append"
                                              + " /flp1:warningsonly;LogFile=" + LogFileNameWithoutExtention + "Warnings.log;Append"
                                              + " /flp2:errorsonly;LogFile=" + LogFileNameWithoutExtention + "Errors.log;Append";

        public string ProjectFileName { get; set; }
        public string ProjectFolder { get; set; }

        public string ProjectFilePath { get; set; }

        public string ReferenceFolder { get; set; }
        public string OutputFolder { get; set; }

        private static Dictionary<string, CompareMode> SpecialFiles = new Dictionary<string, CompareMode>
        {
            { "bridge.js", CompareMode.Presence},
            { "bridge.min.js", CompareMode.Presence}
        };


        private void GetPaths(string folder)
        {
            ProjectFileName = "test" + ".csproj";
            ProjectFolder = FileHelper.GetRelativeToCurrentDirPath(@"\..\..\TestProjects", folder);

            ProjectFilePath = Path.Combine(ProjectFolder, ProjectFileName);

            OutputFolder = Path.Combine(ProjectFolder, @"Bridge\Output");
            ReferenceFolder = Path.Combine(ProjectFolder, @"Bridge\Reference");
        }

        void LogInfo(string message)
        {
            SimpleLogger.Instance.LogInfo(message);
        }

        [TestFixtureSetUp]
        public void TestFixtureSetUp()
        {
            var logFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), LogFileNameWithoutExtention + ".*", SearchOption.AllDirectories);
            foreach (var logFile in logFiles)
            {
                File.Delete(logFile);
            }
        }

        [TestCase("01", true, false, TestName = "OutputTest 01 - Bridge.json Default")]
        [TestCase("02", false, true, TestName = "OutputTest 02 - Bridge.json outputFormatting Formatted")]
        [TestCase("03", true, true, TestName = "OutputTest 03 - Bridge.json outputFormatting Minified")]
        [TestCase("04", true, true, TestName = "OutputTest 04 - Bridge.json outputBy Class")]
        [TestCase("05", true, true, TestName = "OutputTest 05 - Bridge.json outputBy Namespace")]
        [TestCase("06", true, true, TestName = "OutputTest 06 - Bridge.json outputBy Project")]
        [TestCase("07", true, true, TestName = "OutputTest 07 - Bridge.json module")]
        [TestCase("08", true, true, TestName = "OutputTest 08 - Bridge.json fileNameCasing Lowercase")]
        [TestCase("09", true, true, TestName = "OutputTest 09 - Bridge.json fileNameCasing CamelCase")]
        [TestCase("10", true, true, TestName = "OutputTest 10 - Bridge.json fileNameCasing None")]
        [TestCase("11", true, true, TestName = "OutputTest 11 - Bridge.json generateTypeScript")]
        [TestCase("12", true, true, TestName = "OutputTest 12 - Bridge.json generateDocumentation Full")]
        [TestCase("13", true, true, TestName = "OutputTest 13 - Bridge.json generateDocumentation Basic")]
        [TestCase("14", true, true, TestName = "OutputTest 14 - Bridge.json preserveMemberCase")]
        [TestCase("15", true, true, TestName = "OutputTest 15 - Bridge.json filename")]
        [TestCase("16", true, true, TestName = "OutputTest 16 - Issues")]
        [TestCase("17", true, true, TestName = "OutputTest 17 - Define project constant Bridge#375")]
        public void Test(string folder, bool isToTranslate, bool useSpecialFileCompare)
        {
            GetPaths(folder);

            LogInfo("OutputTest Project " + folder);

            LogInfo("\tProjectFileName " + ProjectFileName);
            LogInfo("\tProjectFolder " + ProjectFolder);

            LogInfo("\tProjectFilePath " + ProjectFilePath);

            LogInfo("\tOutputFolder " + OutputFolder);
            LogInfo("\tReferenceFolder " + ReferenceFolder);

            var translator = new TranslatorRunner()
            {
                ProjectLocation = ProjectFilePath,
                BuildArguments = OutputTest.BuildArguments
            };

            try
            {
                if (isToTranslate)
                {
                    translator.Translate();
                }
                else
                {
                    translator.Build();
                }
            }
            catch (Exception ex)
            {
                Assert.Fail("Could not {0} the project {1}. Exception occurred: {2}.", isToTranslate ? "translate" : "build", folder, ex.Message);
            }

            try
            {
                var comparence = FolderComparer.CompareFolders(this.ReferenceFolder, this.OutputFolder, useSpecialFileCompare ? SpecialFiles : null);

                if (comparence.Any())
                {
                    var sb = new StringBuilder();
                    foreach (var diff in comparence)
                    {
                        sb.AppendLine(diff.ToString());
                    }

                    FolderComparer.LogDifferences("Project " + folder + " differences:", comparence);

                    Assert.Fail(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                Assert.Fail("Could not compare the project {0} output. Exception occurred: {1}.", folder, ex.Message);
            }
        }
    }
}
