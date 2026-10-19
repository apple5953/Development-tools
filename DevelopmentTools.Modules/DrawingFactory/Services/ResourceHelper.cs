using System;
using System.Reflection;
using System.Windows;

namespace BIMDrawingFactory.UI.Services
{
    public static class ResourceHelper
    {
        private static bool _isResourcesMerged;
        private static bool _assemblyResolverRegistered;

        [ThreadStatic]
        private static bool _isResolving;

        public static void RegisterAssemblyResolver()
        {
            if (_assemblyResolverRegistered) return;
            _assemblyResolverRegistered = true;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                if (_isResolving) return null;
                _isResolving = true;
                try
                {
                    var currentAssembly = typeof(ResourceHelper).Assembly;
                    var requestedFullName = args.Name;

                    if (string.IsNullOrEmpty(requestedFullName)) return null;

                    string requestedSimpleName = requestedFullName;
                    int commaIdx = requestedFullName.IndexOf(',');
                    if (commaIdx > 0)
                    {
                        requestedSimpleName = requestedFullName.Substring(0, commaIdx).Trim();
                    }

                    if (string.Equals(currentAssembly.GetName().Name, requestedSimpleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return currentAssembly;
                    }

                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (string.Equals(asm.GetName().Name, requestedSimpleName, StringComparison.OrdinalIgnoreCase))
                        {
                            return asm;
                        }
                    }
                }
                catch
                {
                    // Ignore assembly resolve inspection errors
                }
                finally
                {
                    _isResolving = false;
                }
                return null;
            };
        }

        public static void EnsureResourcesLoaded()
        {
            RegisterAssemblyResolver();

            if (_isResourcesMerged) return;

            try
            {
                if (Application.Current == null)
                {
                    new Application();
                }

                if (Application.Current != null)
                {
                    Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    Application.Current.DispatcherUnhandledException -= OnDispatcherUnhandledException;
                    Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
                }

                var assemblyIdentity = typeof(ResourceHelper).Assembly.GetName();
                var dictUri = new Uri(string.Format(
                    "pack://application:,,,/{0};v{1};component/Themes/Styles.xaml",
                    assemblyIdentity.Name,
                    assemblyIdentity.Version), UriKind.Absolute);
                var dict = new ResourceDictionary { Source = dictUri };

                Application.Current.Resources.MergedDictionaries.Add(dict);
                _isResourcesMerged = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("[ResourceHelper.EnsureResourcesLoaded Exception] {0}", ex));
            }
        }

        public static void LoadVersionedComponent(object component)
        {
            if (component == null) throw new ArgumentNullException("component");
            RegisterAssemblyResolver();
            var type = component.GetType();
            var identity = type.Assembly.GetName();
            string prefix = identity.Name + ".";
            string relativeTypeName = type.FullName != null && type.FullName.StartsWith(prefix, StringComparison.Ordinal)
                ? type.FullName.Substring(prefix.Length)
                : type.Name;
            string resourcePath = relativeTypeName.Replace('.', '/') + ".xaml";
            var uri = new Uri(string.Format("/{0};v{1};component/{2}",
                identity.Name, identity.Version, resourcePath), UriKind.Relative);
            Application.LoadComponent(component, uri);
        }

        private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            System.Diagnostics.Debug.WriteLine(string.Format("[WPF Dispatcher Handled Exception] {0}", e.Exception));
        }
    }
}
