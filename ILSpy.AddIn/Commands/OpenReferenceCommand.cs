﻿using System;
using System.Linq;
using EnvDTE;
using Microsoft.CodeAnalysis;
using Mono.Cecil;
using VSLangProj;

namespace ICSharpCode.ILSpy.AddIn.Commands
{
	internal class OpenReferenceCommand : ILSpyCommand
	{
		private static OpenReferenceCommand instance;

		public OpenReferenceCommand(ILSpyAddInPackage owner)
			: base(owner, PkgCmdIDList.cmdidOpenReferenceInILSpy)
		{
		}

		protected override void OnExecute(object sender, EventArgs e)
		{
			var explorer = owner.DTE.ToolWindows.SolutionExplorer;
			var item = ((object[])explorer.SelectedItems).FirstOrDefault() as UIHierarchyItem;

			if (item == null) return;
			if (item.Object is Reference reference) {
				var project = reference.ContainingProject;
				var roslynProject = owner.Workspace.CurrentSolution.Projects.FirstOrDefault(p => p.FilePath == project.FileName);
				var references = GetReferences(roslynProject);
				if (references.TryGetValue(reference.Name, out var path))
					OpenAssembliesInILSpy(new[] { path });
				else
					owner.ShowMessage("Could not find reference '{0}', please ensure the project and all references were built correctly!", reference.Name);
			} else {
				dynamic referenceObject = item.Object;
				if (TryGetProjectFileName(referenceObject, out string fileName)) {
					var roslynProject = owner.Workspace.CurrentSolution.Projects.FirstOrDefault(p => p.FilePath == fileName);
					var references = GetReferences(roslynProject);
					if (references.TryGetValue(referenceObject.Name, out string path)) {
						OpenAssembliesInILSpy(new[] { path });
						return;
					}
				} else {
					var values = GetProperties(referenceObject.Properties, "Type", "FusionName", "ResolvedPath");
					if (values[0] == "Package") {
						values = GetProperties(referenceObject.Properties, "Name", "Version", "Path");
						if (values[0] != null && values[1] != null && values[2] != null) {
							OpenAssembliesInILSpy(new[] { $"{values[2]}\\{values[0]}.{values[1]}.nupkg" });
							return;
						}
					} else if (values[2] != null) {
						OpenAssembliesInILSpy(new[] { $"{values[2]}" });
						return;
					} else if (!string.IsNullOrWhiteSpace(values[1])) {
						OpenAssembliesInILSpy(new string[] { GacInterop.FindAssemblyInNetGac(AssemblyNameReference.Parse(values[1])) });
						return;
					}
				}
				owner.ShowMessage("Could not find reference '{0}', please ensure the project and all references were built correctly!", referenceObject.Name);
			}
		}

		private bool TryGetProjectFileName(dynamic referenceObject, out string fileName)
		{
			try {
				fileName = referenceObject.Project.FileName;
				return true;
			} catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {
				fileName = null;
				return false;
			}
		}

		private string[] GetProperties(Properties properties, params string[] names)
		{
			string[] values = new string[names.Length];
			foreach (dynamic p in properties) {
				try {
					for (int i = 0; i < names.Length; i++) {
						if (names[i] == p.Name) {
							values[i] = p.Value;
							break;
						}
					}
				} catch {
					continue;
				}
			}
			return values;
		}

		private object GetPropertyObject(EnvDTE.Properties properties, string name)
		{
			foreach (dynamic p in properties) {
				try {
					if (name == p.Name) {
						return p.Object;
					}
				} catch {
					continue;
				}
			}
			return null;
		}

		private bool HasProperties(EnvDTE.Properties properties, params string[] names)
		{
			return properties.Count > 0 && names.Any(n => HasProperty(properties, n));
		}

		private bool HasProperty(EnvDTE.Properties properties, string name)
		{
			foreach (dynamic p in properties) {
				try {
					if (name == p.Name) {
						return true;
					}
				} catch {
					continue;
				}
			}
			return false;
		}

		internal static void Register(ILSpyAddInPackage owner)
		{
			instance = new OpenReferenceCommand(owner);
		}
	}
}