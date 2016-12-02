using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web;
using Microsoft.Web.Infrastructure.DynamicModuleHelper;
using Moq;
using Ninject;
using Ninject.Extensions.Conventions;
using Ninject.Extensions.NamedScope;
using Ninject.Web.Common;
using Rubberduck.Inspections;
using Rubberduck.Parsing;
using Rubberduck.Parsing.VBA;
using Rubberduck.VBEditor.Application;
using Rubberduck.VBEditor.Events;
using RubberduckWeb;

[assembly: WebActivatorEx.PreApplicationStartMethod(typeof(NinjectWebCommon), "Start")]
[assembly: WebActivatorEx.ApplicationShutdownMethod(typeof(NinjectWebCommon), "Stop")]

namespace RubberduckWeb
{
    public static class NinjectWebCommon 
    {
        private static readonly Bootstrapper Bootstrapper = new Bootstrapper();
        private static readonly ISinks Sinks = new Mock<ISinks>().Object;

        /// <summary>
        /// Starts the application
        /// </summary>
        public static void Start() 
        {
            DynamicModuleUtility.RegisterModule(typeof(OnePerRequestHttpModule));
            DynamicModuleUtility.RegisterModule(typeof(NinjectHttpModule));
            Bootstrapper.Initialize(CreateKernel);
        }
        
        /// <summary>
        /// Stops the application.
        /// </summary>
        public static void Stop()
        {
            Bootstrapper.ShutDown();
        }
        
        /// <summary>
        /// Creates the kernel that will manage your application.
        /// </summary>
        /// <returns>The created kernel.</returns>
        private static IKernel CreateKernel()
        {
            var kernel = new StandardKernel();
            try
            {
                kernel.Bind<Func<IKernel>>().ToMethod(ctx => () => new Bootstrapper().Kernel);
                kernel.Bind<IHttpModule>().To<HttpApplicationInitializationHttpModule>();

                RegisterServices(kernel);
                return kernel;
            }
            catch
            {
                kernel.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Load your modules or register your services here!
        /// </summary>
        /// <param name="kernel">The kernel.</param>
        private static void RegisterServices(IKernel kernel)
        {
            var assemblies = new[]
            {
                Assembly.GetExecutingAssembly(),
                Assembly.GetAssembly(typeof(IHostApplication)), // Rubberduck.VBEditor
                Assembly.GetAssembly(typeof(InspectionBase)),   // Rubberduck
                Assembly.GetAssembly(typeof(IRubberduckParser)) // Rubberduck.Parsing
            };
            ApplyDefaultInterfacesConvention(kernel, assemblies);

            kernel.Rebind<ISinks>().ToConstant(Sinks);
            kernel.Bind<RubberduckParserState>().ToSelf().InRequestScope();
            kernel.Bind<IRubberduckParser>().To<RubberduckParser>().InRequestScope();

            BindCodeInspectionTypes(kernel);
        }

        private static void ApplyDefaultInterfacesConvention(IKernel kernel, IEnumerable<Assembly> assemblies)
        {
            kernel.Bind(t => t.From(assemblies)
                .SelectAllClasses()
                // inspections & factories have their own binding rules
                .Where(type => !type.Name.EndsWith("Factory") && !type.Name.EndsWith("ConfigProvider") && !type.GetInterfaces().Contains(typeof(IInspection)))
                .BindDefaultInterface()
                .Configure(binding => binding.InRequestScope())); // TransientScope wouldn't dispose disposables
        }

        private static void BindCodeInspectionTypes(IKernel kernel)
        {
            var assembly = Assembly.GetAssembly(typeof (InspectionBase));
            var inspections = assembly.GetTypes().Where(type => type.BaseType == typeof(InspectionBase));

            // skip inspections with undesirable dependencies
            foreach (var inspection in inspections)
            {
                if (inspection.Name == nameof(ImplicitActiveSheetReferenceInspection) ||
                    inspection.Name == nameof(ImplicitActiveWorkbookReferenceInspection) ||
                    inspection.Name == nameof(ParameterNotUsedInspection) ||
                    inspection.Name == nameof(EncapsulatePublicFieldInspection) ||
                    inspection.Name == nameof(UseMeaningfulNameInspection))
                {
                    continue;
                }

                kernel.Bind<IInspection>().To(inspection).InSingletonScope();
            }
        }
    }
}
