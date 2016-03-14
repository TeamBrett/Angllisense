using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using EnvDTE;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Angllisense.Services {
    public class TypeScriptFile {
        public string RawText { get; set; }
        public TypeScriptCodeModel Model { get; set; }
    }

    public static class ProjectNavigator {
        public static List<FileInfo> GetAllTypeScriptFiles(SVsServiceProvider serviceProvider) {
            var solution = serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;

            var projectItems = new List<TypeScriptFile>();
            foreach (Project project in GetProjects(solution)) {
                foreach (ProjectItem projectItem in project.ProjectItems) {
                    var item = FlattenProjectItems(projectItem);
                    if (item != null && item.Name.EndsWith(".ts")) {
                        var typeScriptFile = new TypeScriptFile { RawText = File.ReadAllText(item.Document.FullName), };
                        typeScriptFile.Model = new TypeScriptParser().Parse(typeScriptFile.RawText);
                        projectItems.Add(typeScriptFile);
                    }
                }
            }

            return new List<FileInfo>();
        }

        private static ProjectItem FlattenProjectItems(ProjectItem item) {
            if (item.ProjectItems == null || item.ProjectItems.Count <= 0) {
                return item;
            }

            foreach (ProjectItem projectItem in item.ProjectItems) {
                return FlattenProjectItems(projectItem);
            }

            return null;
        }

        public static IEnumerable<Project> GetProjects(IVsSolution solution) {
            foreach (IVsHierarchy hier in GetProjectsInSolution(solution)) {
                var project = GetDTEProject(hier);
                if (project != null) {
                    yield return project;
                }
            }
        }

        public static IEnumerable<IVsHierarchy> GetProjectsInSolution(IVsSolution solution) {
            return GetProjectsInSolution(solution, __VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION);
        }

        public static IEnumerable<IVsHierarchy> GetProjectsInSolution(IVsSolution solution, __VSENUMPROJFLAGS flags) {
            if (solution == null) {
                yield break;
            }

            IEnumHierarchies enumHierarchies;
            var guid = Guid.Empty;
            solution.GetProjectEnum((uint)flags, ref guid, out enumHierarchies);
            if (enumHierarchies == null) {
                yield break;
            }

            IVsHierarchy[] hierarchy = new IVsHierarchy[1];
            uint fetched;
            while (enumHierarchies.Next(1, hierarchy, out fetched) == VSConstants.S_OK && fetched == 1) {
                if (hierarchy.Length > 0 && hierarchy[0] != null) {
                    yield return hierarchy[0];
                }
            }
        }

        public static Project GetDTEProject(IVsHierarchy hierarchy) {
            if (hierarchy == null) {
                throw new ArgumentNullException(nameof(hierarchy));
            }

            object obj;
            hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out obj);
            return obj as Project;
        }

        public static bool IsSingleProjectItemSelection(ServiceProvider serviceProvider, out IVsHierarchy hierarchy, out uint itemid) {
            hierarchy = null;
            itemid = VSConstants.VSITEMID_NIL;

            var monitorSelection = serviceProvider.GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            var solution = serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
            if (monitorSelection == null || solution == null) {
                return false;
            }

            var hierarchyPtr = IntPtr.Zero;
            var selectionContainerPtr = IntPtr.Zero;

            try {
                IVsMultiItemSelect multiItemSelect;
                int hr = monitorSelection.GetCurrentSelection(out hierarchyPtr, out itemid, out multiItemSelect, out selectionContainerPtr);

                if (ErrorHandler.Failed(hr) || hierarchyPtr == IntPtr.Zero || itemid == VSConstants.VSITEMID_NIL) {
                    // there is no selection
                    return false;
                }

                // multiple items are selected
                if (multiItemSelect != null) {
                    return false;
                }

                // there is a hierarchy root node selected, thus it is not a single item inside a project

                if (itemid == VSConstants.VSITEMID_ROOT) {
                    return false;
                }

                hierarchy = Marshal.GetObjectForIUnknown(hierarchyPtr) as IVsHierarchy;
                if (hierarchy == null) {
                    return false;
                }

                Guid guidProjectId;
                if (ErrorHandler.Failed(solution.GetGuidOfProject(hierarchy, out guidProjectId))) {
                    return false; // hierarchy is not a project inside the Solution if it does not have a ProjectID Guid
                }

                // if we got this far then there is a single project item selected
                return true;
            } finally {
                if (selectionContainerPtr != IntPtr.Zero) {
                    Marshal.Release(selectionContainerPtr);
                }

                if (hierarchyPtr != IntPtr.Zero) {
                    Marshal.Release(hierarchyPtr);
                }
            }
        }
    }
}
