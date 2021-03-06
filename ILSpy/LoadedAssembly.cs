﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler;
using ICSharpCode.ILSpy.Options;
using Mono.Cecil;

namespace ICSharpCode.ILSpy
{
	/// <summary>
	/// Represents an assembly loaded into ILSpy.
	/// </summary>
	public sealed class LoadedAssembly
	{
		private readonly Task<ModuleDefinition> assemblyTask;
		private readonly AssemblyList assemblyList;
		private readonly string fileName;
		private readonly string shortName;

		public LoadedAssembly(AssemblyList assemblyList, string fileName, Stream stream = null)
		{
			if (assemblyList == null)
				throw new ArgumentNullException(nameof(assemblyList));
			if (fileName == null)
				throw new ArgumentNullException(nameof(fileName));
			this.assemblyList = assemblyList;
			this.fileName = fileName;

			this.assemblyTask = Task.Factory.StartNew<ModuleDefinition>(LoadAssembly, stream); // requires that this.fileName is set
			this.shortName = Path.GetFileNameWithoutExtension(fileName);
		}

		/// <summary>
		/// Returns a target framework identifier in the form '&lt;framework&gt;Version=v&lt;version&gt;'.
		/// Returns an empty string if no TargetFrameworkAttribute was found or the file doesn't contain an assembly header, i.e., is only a module.
		/// </summary>
		public async Task<string> GetTargetFrameworkIdAsync()
		{
			var assembly = await GetAssemblyDefinitionAsync().ConfigureAwait(false);
			return assembly?.DetectTargetFrameworkId() ?? string.Empty;
		}

		public ReferenceLoadInfo LoadedAssemblyReferencesInfo { get; } = new ReferenceLoadInfo();

		/// <summary>
		/// Gets the Cecil ModuleDefinition.
		/// </summary>
		public Task<ModuleDefinition> GetModuleDefinitionAsync()
		{
			return assemblyTask;
		}

		/// <summary>
		/// Gets the Cecil ModuleDefinition.
		/// Returns null in case of load errors.
		/// </summary>
		public ModuleDefinition GetModuleDefinitionOrNull()
		{
			try {
				return GetModuleDefinitionAsync().Result;
			} catch (Exception ex) {
				System.Diagnostics.Trace.TraceError(ex.ToString());
				return null;
			}
		}

		/// <summary>
		/// Gets the Cecil AssemblyDefinition.
		/// </summary>
		public async Task<AssemblyDefinition> GetAssemblyDefinitionAsync()
		{
			var module = await assemblyTask.ConfigureAwait(false);
			return module != null ? module.Assembly : null;
		}

		/// <summary>
		/// Gets the Cecil AssemblyDefinition.
		/// Returns null when there was a load error; or when opening a netmodule.
		/// </summary>
		public AssemblyDefinition GetAssemblyDefinitionOrNull()
		{
			try {
				return GetAssemblyDefinitionAsync().Result;
			} catch (Exception ex) {
				System.Diagnostics.Trace.TraceError(ex.ToString());
				return null;
			}
		}

		public AssemblyList AssemblyList => assemblyList;

		public string FileName => fileName;

		public string ShortName => shortName;

		public string Text {
			get {
				if (IsLoaded && !HasLoadError) {
					string version = GetAssemblyDefinitionOrNull()?.Name.Version.ToString();
					if (version == null)
						return ShortName;
					return String.Format("{0} ({1})", ShortName, version);
				} else {
					return ShortName;
				}
			}
		}

		public bool IsLoaded => assemblyTask.IsCompleted;

		public bool HasLoadError => assemblyTask.IsFaulted;

		public bool IsAutoLoaded { get; set; }

		private ModuleDefinition LoadAssembly(object state)
		{
			var stream = state as Stream;
			ModuleDefinition module;

			// runs on background thread
			ReaderParameters p = new ReaderParameters();
			p.AssemblyResolver = new MyAssemblyResolver(this);
			p.InMemory = true;

			if (stream != null) {
				// Read the module from a precrafted stream
				module = ModuleDefinition.ReadModule(stream, p);
			} else {
				// Read the module from disk (by default)
				module = ModuleDefinition.ReadModule(fileName, p);
			}

			if (DecompilerSettingsPanel.CurrentDecompilerSettings.UseDebugSymbols) {
				try {
					LoadSymbols(module);
				} catch (IOException) {
				} catch (UnauthorizedAccessException) {
				} catch (InvalidOperationException) {
					// ignore any errors during symbol loading
				}
			}
			return module;
		}

		private void LoadSymbols(ModuleDefinition module)
		{
			if (!module.HasDebugHeader) {
				return;
			}

			// search for pdb in same directory as dll
			string pdbName = Path.Combine(Path.GetDirectoryName(fileName), Path.GetFileNameWithoutExtension(fileName) + ".pdb");
			if (File.Exists(pdbName)) {
				using (Stream s = File.OpenRead(pdbName)) {
					module.ReadSymbols(new Mono.Cecil.Pdb.PdbReaderProvider().GetSymbolReader(module, s));
				}
				return;
			}

			// TODO: use symbol cache, get symbols from microsoft
		}

		[ThreadStatic]
		private static int assemblyLoadDisableCount;

		public static IDisposable DisableAssemblyLoad()
		{
			assemblyLoadDisableCount++;
			return new DecrementAssemblyLoadDisableCount();
		}

		private sealed class DecrementAssemblyLoadDisableCount : IDisposable
		{
			private bool disposed;

			public void Dispose()
			{
				if (!disposed) {
					disposed = true;
					assemblyLoadDisableCount--;
					// clear the lookup cache since we might have stored the lookups failed due to DisableAssemblyLoad()
					MainWindow.Instance.CurrentAssemblyList.ClearCache();
				}
			}
		}

		private sealed class MyAssemblyResolver : IAssemblyResolver
		{
			private readonly LoadedAssembly parent;

			public MyAssemblyResolver(LoadedAssembly parent)
			{
				this.parent = parent;
			}

			public AssemblyDefinition Resolve(AssemblyNameReference name)
			{
				return parent.LookupReferencedAssembly(name)?.GetAssemblyDefinitionOrNull();
			}

			public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
			{
				return parent.LookupReferencedAssembly(name)?.GetAssemblyDefinitionOrNull();
			}

			public void Dispose()
			{
			}
		}

		public IAssemblyResolver GetAssemblyResolver()
		{
			return new MyAssemblyResolver(this);
		}

		public LoadedAssembly LookupReferencedAssembly(AssemblyNameReference name)
		{
			if (name == null)
				throw new ArgumentNullException(nameof(name));
			if (name.IsWindowsRuntime) {
				return assemblyList.assemblyLookupCache.GetOrAdd((name.Name, true), key => LookupReferencedAssemblyInternal(name, true));
			} else {
				return assemblyList.assemblyLookupCache.GetOrAdd((name.FullName, false), key => LookupReferencedAssemblyInternal(name, false));
			}
		}

		private class MyUniversalResolver : UniversalAssemblyResolver
		{
			public MyUniversalResolver(LoadedAssembly assembly)
				: base(assembly.FileName, false)
			{
			}
		}

		private static Dictionary<string, LoadedAssembly> loadingAssemblies = new Dictionary<string, LoadedAssembly>();

		private LoadedAssembly LookupReferencedAssemblyInternal(AssemblyNameReference fullName, bool isWinRT)
		{
			string GetName(AssemblyNameReference name) => isWinRT ? name.Name : name.FullName;

			string file;
			LoadedAssembly asm;
			lock (loadingAssemblies) {
				foreach (LoadedAssembly loaded in assemblyList.GetAssemblies()) {
					var asmDef = loaded.GetAssemblyDefinitionOrNull();
					if (asmDef != null && GetName(fullName).Equals(GetName(asmDef.Name), StringComparison.OrdinalIgnoreCase)) {
						LoadedAssemblyReferencesInfo.AddMessageOnce(fullName.ToString(), MessageKind.Info, "Success - Found in Assembly List");
						return loaded;
					}
				}

				if (isWinRT) {
					file = Path.Combine(Environment.SystemDirectory, "WinMetadata", fullName.Name + ".winmd");
				} else {
					var resolver = new MyUniversalResolver(this) { TargetFramework = GetTargetFrameworkIdAsync().Result };
					file = resolver.FindAssemblyFile(fullName);
				}

				foreach (LoadedAssembly loaded in assemblyList.GetAssemblies()) {
					if (loaded.FileName.Equals(file, StringComparison.OrdinalIgnoreCase)) {
						return loaded;
					}
				}

				if (file != null && loadingAssemblies.TryGetValue(file, out asm))
					return asm;

				if (assemblyLoadDisableCount > 0)
					return null;

				if (file != null) {
					LoadedAssemblyReferencesInfo.AddMessage(fullName.ToString(), MessageKind.Info, "Success - Loading from: " + file);
					asm = new LoadedAssembly(assemblyList, file) { IsAutoLoaded = true };
				} else {
					LoadedAssemblyReferencesInfo.AddMessageOnce(fullName.ToString(), MessageKind.Error, "Could not find reference: " + fullName);
					return null;
				}
				loadingAssemblies.Add(file, asm);
			}
			App.Current.Dispatcher.BeginInvoke((Action)delegate () {
				lock (assemblyList.assemblies) {
					assemblyList.assemblies.Add(asm);
				}
				lock (loadingAssemblies) {
					loadingAssemblies.Remove(file);
				}
			});
			return asm;
		}

		public Task ContinueWhenLoaded(Action<Task<ModuleDefinition>> onAssemblyLoaded, TaskScheduler taskScheduler)
		{
			return this.assemblyTask.ContinueWith(onAssemblyLoaded, default(CancellationToken), TaskContinuationOptions.RunContinuationsAsynchronously, taskScheduler);
		}

		/// <summary>
		/// Wait until the assembly is loaded.
		/// Throws an AggregateException when loading the assembly fails.
		/// </summary>
		public void WaitUntilLoaded()
		{
			assemblyTask.Wait();
		}
	}
}