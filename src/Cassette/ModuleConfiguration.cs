﻿// CreateModuleContainer    = (useCache, applicationVersion) => ModuleContainer<ModuleType>
using System;
using System.Collections.Generic;
using System.Linq;
using CreateModuleContainer = System.Func<bool, string, Cassette.ISearchableModuleContainer<Cassette.Module>>;

namespace Cassette
{
    public class ModuleConfiguration
    {
        public ModuleConfiguration(ICassetteApplication application, IFileSystem cacheDirectory, Dictionary<Type, object> moduleFactories)
        {
            this.application = application;
            this.cacheDirectory = cacheDirectory;
            this.moduleFactories = moduleFactories;
        }

        readonly ICassetteApplication application;
        readonly Dictionary<Type, Tuple<object, CreateModuleContainer>> moduleSourceResultsByType = new Dictionary<Type, Tuple<object, CreateModuleContainer>>();
        readonly IFileSystem cacheDirectory;
        readonly Dictionary<Type, object> moduleFactories;
        readonly Dictionary<Type, List<Action<object>>> customizations = new Dictionary<Type, List<Action<object>>>();

        public void Add<T>(params IModuleSource<T>[] moduleSources)
            where T : Module
        {
            foreach (var moduleSource in moduleSources)
            {
                Add(moduleSource);
            }
        }

        public bool ContainsModuleSources(Type moduleType)
        {
            return moduleSourceResultsByType.ContainsKey(moduleType);
        }

        void Add<T>(IModuleSource<T> moduleSource)
            where T : Module
        {
            var result = moduleSource.GetModules(GetModuleFactory<T>(), application);

            Tuple<object, CreateModuleContainer> existingTuple;
            if (moduleSourceResultsByType.TryGetValue(typeof(T), out existingTuple))
            {
                var existingResult = (ModuleSourceResult<T>)existingTuple.Item1;
                var existingAction = existingTuple.Item2;
                // Update the existing result by merging in the new result.
                // Keep the existing initialization action.
                moduleSourceResultsByType[typeof(T)] = Tuple.Create(
                    (object)existingResult.Merge(result),
                    existingAction
                );
            }
            else
            {
                moduleSourceResultsByType[typeof(T)] = Tuple.Create<object, CreateModuleContainer>(
                    result,
                    (useCache, applicationVersion) => CreateModuleContainer<T>(useCache, applicationVersion)
                );
            }
        }

        public Dictionary<Type, ISearchableModuleContainer<Module>> CreateModuleContainers(bool useCache, string applicationVersion)
        {
            return moduleSourceResultsByType.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Item2(useCache, applicationVersion)
            );
        }

        IModuleContainer<T> CreateModuleContainer<T>(bool useCache, string applicationVersion)
            where T : Module
        {
            var finalResult = (ModuleSourceResult<T>)moduleSourceResultsByType[typeof(T)].Item1;
            if (useCache)
            {
                return GetOrCreateCachedModuleContainer(finalResult, applicationVersion);
            }
            else
            {
                return CreateModuleContainer(finalResult.Modules);
            }
        }

        IModuleContainer<T> GetOrCreateCachedModuleContainer<T>(ModuleSourceResult<T> finalResult, string applicationVersion) where T : Module
        {
            var cache = GetModuleCache<T>();
            if (cache.IsUpToDate(finalResult.LastWriteTimeMax, applicationVersion, application.RootDirectory))
            {
                var loadedModules = cache.LoadModules();
                var nonPersistentModules = finalResult.Modules.Where(m => m.IsPersistent == false);
                return new ModuleContainer<T>(
                    loadedModules.Concat(nonPersistentModules)
                );
            }
            else
            {
                var container = CreateModuleContainer(finalResult.Modules);
                cache.SaveModuleContainer(container, applicationVersion);
                return container;
                // TODO: perhaps return the cached container instance instead?
                // This may be more consistent with the cache-hit scenario above.
            }
        }

        ModuleContainer<T> CreateModuleContainer<T>(IEnumerable<T> modules) where T : Module
        {
            var modulesArray = modules.ToArray();
            List<Action<object>> customizeActions;
            if (customizations.TryGetValue(typeof(T), out customizeActions))
            {
                foreach (var customize in customizeActions)
                {
                    foreach (var module in modulesArray)
                    {
                        customize(module);
                    }
                }
            }
            ProcessAll(modulesArray);
            return new ModuleContainer<T>(modulesArray);
        }

        void ProcessAll<T>(IEnumerable<T> modules)
            where T : Module
        {
            foreach (var module in modules)
            {
                module.Process(application);
            }
        }

        IModuleCache<T> GetModuleCache<T>()
            where T : Module
        {
            return new ModuleCache<T>(
                cacheDirectory.NavigateTo(typeof(T).Name, true),
                GetModuleFactory<T>()
            );
        }

        IModuleFactory<T> GetModuleFactory<T>()
            where T : Module
        {
            return (IModuleFactory<T>)moduleFactories[typeof(T)];
        }

        public void Customize<T>(Action<T> action)
            where T : Module
        {
            var list = GetOrCreateCustomizationList<T>();
            list.Add(module => action((T)module));
        }

        public void Customize<T>(Func<T, bool> predicate, Action<T> action)
            where T : Module
        {
            var list = GetOrCreateCustomizationList<T>();
            list.Add(module =>
            {
                var typedModule = (T)module;
                if (predicate(typedModule)) action(typedModule);
            });
        }

        List<Action<object>> GetOrCreateCustomizationList<T>()
            where T : Module
        {
            List<Action<object>> list;
            if (customizations.TryGetValue(typeof(T), out list) == false)
            {
                customizations[typeof(T)] = list = new List<Action<object>>();
            }
            return list;
        }
    }
}