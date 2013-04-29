using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Cassette.IO;
using LibSass;

namespace Cassette.Stylesheets
{
    /// <summary>
    /// Adapt Cassette to use libsass vis NSass.
    /// Roughly based on NSass.SassCompiler.
    /// </summary>
    public class LibSassCompiler : ISassCompiler
    {
        readonly object _lock = new object();
        readonly ISassInterface sassInterface = new SassInterface();

        static LibSassCompiler()
        {
            AssemblyResolver.Initialize();
        }

        public CompileResult Compile(string source, CompileContext context)
        {
            var sourceFile = context.RootDirectory.GetFile(context.SourceFilePath);
            lock (_lock)
            {
                var sassContext = new SassContext
                {
                    SourceString = source,
                    Options = new SassOptions
                    {
                        OutputStyle = (int)OutputStyle.Nested,
                        SourceComments = false,
                        IncludePaths = sourceFile.Directory.GetAbsolutePath()
                    }
                };

                sassInterface.Compile(sassContext);
                
                if (sassContext.ErrorStatus)
                {
                    throw new Exception(string.Format("Error in {0}: {1}", sourceFile, sassContext.ErrorMessage));
                }

                return new CompileResult(sassContext.OutputString, GetReferencesViaRegexHack(source, sourceFile));
            }
        }

        static readonly Regex ReferencexRegex = new Regex(@"@import\s+""(?<Filename>[^""]*)""", RegexOptions.Compiled);

        /// <summary>
        /// Recursively look for include statements to find referenced files
        /// This should really come from libsass
        /// </summary>
        /// <param name="source"></param>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        static IEnumerable<string> GetReferencesViaRegexHack(string source, IFile sourceFile)
        {
            if (!sourceFile.Exists)
                return null;
            var result = new List<string> { sourceFile.FullPath };
            foreach (Match match in ReferencexRegex.Matches(source))
            {
                var includedFilename = match.Groups["Filename"].Value;
                var includedFile = sourceFile.Directory.GetFile(includedFilename);
                // SCSS allows include directives to omit file extensions...
                if (!includedFile.Exists)
                    includedFile = sourceFile.Directory.GetFile(includedFilename + sourceFile.GetExtension());
                result.AddRange(GetReferencesViaRegexHack(new StreamReader(sourceFile.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite)).ReadToEnd(), includedFile));
            }
            return result;
        }
    }

    public static class Extensions
    {
        public static string GetExtension(this IFile file)
        {
            var dotIndex = file.FullPath.LastIndexOf('.');
            return dotIndex > 0 ? file.FullPath.Substring(dotIndex) : null;
        }
    }
}
