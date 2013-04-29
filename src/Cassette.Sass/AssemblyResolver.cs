using System;
using System.IO;
using System.Reflection;

namespace Cassette.Stylesheets
{
	public static class AssemblyResolver
	{
		private const string AssemblyName = "Cassette.Sass.LibSass.Wrapper";

        /// <summary>
        /// Load assembly for current architecture
        /// </summary>
		public static void Initialize()
		{
			var assemblyDir = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
			var proxyFullPath = Path.Combine(assemblyDir, String.Format("{0}.proxy.dll", AssemblyName));
			if (File.Exists(proxyFullPath))
			{
				throw new InvalidOperationException(String.Format("Found {0}.proxy.dll which cannot exist. Check your references." + assemblyDir, AssemblyName));
			}

			AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
			{
			    if (args.Name.StartsWith(String.Format("{0}.proxy,", AssemblyName), StringComparison.OrdinalIgnoreCase))
			    {
			        var resourceName = String.Format("{0}.{1}.{2}.dll", "Cassette.Stylesheets.Resources", AssemblyName,
			                                         (IntPtr.Size == 8) ? "x64" : "x86");
			        var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
			        if (resourceStream == null)
			            throw new FileLoadException(resourceName);

			        var bytes = new byte[resourceStream.Length];
			        resourceStream.Read(bytes, 0, (int)resourceStream.Length);

			        var tmpAssemblyFilePath = Path.GetTempFileName();
			        File.WriteAllBytes(tmpAssemblyFilePath, bytes);

			        return Assembly.LoadFile(tmpAssemblyFilePath);
			    }
			    return null;
			};
		}
	}
}