﻿using Bridge.Contract;
using Microsoft.Ajax.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Bridge.Translator
{
    public partial class Translator : ITranslator
    {
        public const string Bridge_ASSEMBLY = "Bridge";

        public Translator(string location, bool fromTask = false)
        {
            this.Location = location;
            this.Validator = this.CreateValidator();
            this.DefineConstants = new List<string>(){"BRIDGE"};
            this.FromTask = fromTask;
        }

        public Translator(string folder, string source, bool recursive, string lib)
        {
            this.Recursive = recursive;
            this.Source = source;
            this.FolderMode = true;
            this.Location = folder;
            this.AssemblyLocation = lib;
            this.Validator = this.CreateValidator();
            this.DefineConstants = new List<string>() { "BRIDGE" };
        }

        public Dictionary<string, string> Translate()
        {
            var config = this.ReadConfig();

            if (!string.IsNullOrWhiteSpace(config.Configuration))
            {
                this.Configuration = config.Configuration;
            }

            if (config.DefineConstants != null && config.DefineConstants.Count > 0)
            {
                this.DefineConstants.AddRange(config.DefineConstants);
                this.DefineConstants = this.DefineConstants.Distinct().ToList();
            }

            this.Plugins = Bridge.Translator.Plugins.GetPlugins(this, config);
            this.Plugins.OnConfigRead(config);

            if (config != null && !string.IsNullOrWhiteSpace(config.BeforeBuild))
            {
                try
                {
                    this.RunEvent(config.BeforeBuild);
                }
                catch (Exception exc)
                {
                    throw new Bridge.Translator.Exception("Error: Unable to run beforeBuild event command: " +
                        exc.Message + "\nStack trace:\n" + exc.StackTrace);
                }
            }

            if (this.FolderMode)
            {
                this.ReadFolderFiles();
            }
            else
            {
                this.ReadProjectFile();

                if (this.Rebuild || !File.Exists(this.AssemblyLocation))
                {
                    this.BuildAssembly();
                }
            }

            var references = this.InspectReferences();

            this.BuildSyntaxTree();
            var resolver = new MemberResolver(this.ParsedSourceFiles, Emitter.ToAssemblyReferences(references));

            this.InspectTypes(resolver, config);

            resolver.CanFreeze = true;
            var emitter = this.CreateEmitter(resolver);
            emitter.Translator = this;
            emitter.AssemblyInfo = this.AssemblyInfo;
            emitter.References = references;
            emitter.SourceFiles = this.SourceFiles;
            emitter.Log = this.Log;
            emitter.Plugins = this.Plugins;
            this.Plugins.BeforeEmit(emitter, this);
            this.Outputs = emitter.Emit();
            this.Plugins.AfterEmit(emitter, this);

            return this.Outputs;
        }

        public virtual string GetCode()
        {
            StringBuilder builder = new StringBuilder();

            foreach (var item in this.Outputs)
            {
                string code = item.Value;
                builder.AppendLine(code);
            }

            return builder.ToString();
        }

        public virtual void SaveTo(string path, string defaultFileName)
        {
            var minifier = new Minifier();
            foreach (var item in this.Outputs)
            {
                string fileName = item.Key;
                string code = item.Value;

                if (fileName.Contains(Bridge.Translator.AssemblyInfo.DEFAULT_FILENAME))
                {
                    fileName = fileName.Replace(Bridge.Translator.AssemblyInfo.DEFAULT_FILENAME, defaultFileName);
                }

                // Ensure filename contains no ":". It could be used like "c:/absolute/path"
                fileName = fileName.Replace(":", "_");

                // Trim heading slash/backslash off file names until it does not start with slash.
                var oldFNlen = fileName.Length;
                while (Path.IsPathRooted(fileName))
                {
                    fileName = fileName.TrimStart(Path.DirectorySeparatorChar, '/', '\\');

                    // Trimming didn't change the path. This way, it will just loop indefinitely.
                    // Also, this means the absolute path specifies a fully-qualified DOS PathName with drive letter.
                    if (fileName.Length == oldFNlen)
                    {
                        break;
                    }
                    oldFNlen = fileName.Length;
                }

                // If 'fileName' is an absolute path, Path.Combine will ignore the 'path' prefix.
                string filePath = Path.Combine(path, fileName);
                string extension = Path.GetExtension(filePath);
                bool isJs = extension == ('.' + Bridge.Translator.AssemblyInfo.JAVASCRIPT_EXTENSION);

                System.IO.FileInfo file;

                // We can only have Beautified, Minified or Both, so this test has inverted logic:
                // output beautified if not minified only == (output beautified or output both)
                // Check by @vladsch: Output anyway if the class is not a JavaScript file.
                if (this.AssemblyInfo.OutputFormatting != JavaScriptOutputType.Minified || !isJs)
                {
                    file = new System.IO.FileInfo(filePath);
                    file.Directory.Create();
                    string header = isJs ? "/* global Bridge */\n\n" : "";
                    File.WriteAllText(file.FullName, header + code, System.Text.UTF8Encoding.UTF8);
                }

                // Like above test: output minified if not beautified only == (out minified or out both)
                // Check by @vladsch: Output minified is allowed only and only if it is a JavaScript being output.
                if (this.AssemblyInfo.OutputFormatting != JavaScriptOutputType.Formatted && isJs)
                {
                    fileName = Path.GetFileNameWithoutExtension(filePath) + ".min" + extension;
                    filePath = Path.Combine(Path.GetDirectoryName(filePath), fileName);
                    file = new System.IO.FileInfo(filePath);
                    file.Directory.Create();
                    File.WriteAllText(file.FullName, minifier.MinifyJavaScript(code, new CodeSettings {  TermSemicolons = true }), System.Text.UTF8Encoding.UTF8);
                }
            }

            if (!string.IsNullOrWhiteSpace(this.AssemblyInfo.AfterBuild))
            {
                try
                {
                    this.RunEvent(this.AssemblyInfo.AfterBuild);
                }
                catch (Exception exc)
                {
                    throw new Bridge.Translator.Exception("Error: Unable to run afterBuild event command: " +
                        exc.Message + "\nStack trace:\n" + exc.StackTrace);
                }
            }
        }

        protected virtual Emitter CreateEmitter(IMemberResolver resolver)
        {
            return new Emitter(this.TypeDefinitions, this.BridgeTypes, this.Types, this.Validator, resolver, this.TypeInfoDefinitions);
        }

        protected virtual Validator CreateValidator()
        {
            return new Validator();
        }

        public static void ExtractCore(Translator translatorInstance, string outputPath, bool nodebug = false)
        {
            var clrPath = translatorInstance.BridgeLocation;
            var assembly = System.Reflection.Assembly.UnsafeLoadFrom(clrPath);

            // We can only have Beautified, Minified or Both, so this test has inverted logic:
            // output beautified if not minified only == (output beautified or output both)
            if (translatorInstance.AssemblyInfo.OutputFormatting != JavaScriptOutputType.Minified)
            {
                ExtractResourceAndWriteToFile(outputPath, assembly, "Bridge.Resources.bridge.js", "bridge.js");
            }

            if (translatorInstance.AssemblyInfo.GenerateTypeScript)
            {
                ExtractResourceAndWriteToFile(outputPath, assembly, "Bridge.Resources.bridge.d.ts", "bridge.d.ts");
            }

            // Like above test: output minified if not beautified only == (out minified or out both)
            if (translatorInstance.AssemblyInfo.OutputFormatting != JavaScriptOutputType.Formatted)
            {
                if (!nodebug)
                {
                    ExtractResourceAndWriteToFile(outputPath, assembly, "Bridge.Resources.bridge.js", "bridge.min.js", (reader) => { var minifier = new Minifier(); return minifier.MinifyJavaScript(reader.ReadToEnd(), new CodeSettings { TermSemicolons = true }); });
                }
            }
        }

        private static void ExtractResourceAndWriteToFile(string outputPath, Assembly assembly, string resourceName, string fileName, Func<StreamReader, string> preHandler = null)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    EnsureDirectoryExistsCreateAndWriteFile(outputPath, reader, fileName, preHandler);
                }
            }
        }

        private static void EnsureDirectoryExistsCreateAndWriteFile(string outputPath, StreamReader reader, string fileName, Func<StreamReader, string> preHandler)
        {
            var filePath = Path.Combine(outputPath, fileName);

            var file = new System.IO.FileInfo(filePath);
            file.Directory.Create();

            var content = preHandler != null ? preHandler(reader) : reader.ReadToEnd();

            File.WriteAllText(file.FullName, content, System.Text.UTF8Encoding.UTF8);
        }

        public EmitterException CreateExceptionFromLastNode()
        {
            return this.EmitNode != null ? new EmitterException(this.EmitNode) : null;
        }
    }
}
