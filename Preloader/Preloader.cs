namespace Preloader
{
    using Steamworks;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using UnityEngine;

    public class Preloader : AbstractMod
    {
        private int identifierCalls = 0;

        public override string getName()
        {
            return "PRELOADER FAILED";
        }

        public override string getDescription()
        {
            return "PRELOADER FAILED";
        }

        public override string getVersionNumber()
        {
            return "BROKEN";
        }

        public override string getIdentifier()
        {
            // GIGA HACK
            if (!initialized && identifierCalls < 2)
            {
                identifierCalls++;
                // first 2 calls by ModManager, return gibberish
                return tempGuid.ToString();
            }

            if (!initialized)
            {
                //third call, now the mod is registered with ModManager and we can get path to the mod.
                var entries = ModManager.Instance.getModEntries();
                ModManager.ModEntry entry = null;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (this == entries[i].mod)
                    {
                        entry = entries[i];
                        break;
                    }
                }

                modPath = entry.path;
                // Load descriptor, look for dependencies, load assemblies from other mods, find the actual mod and create an instance,
                // substitute preloader mod with actual mod.
                Preload(entry, modPath);
                if (initialized)
                {
                    Debug.Log("Preloader: Registering mod by id " + innerMod.getIdentifier());
                    return innerMod.getIdentifier();
                }
            }

            return tempGuid.ToString();
        }

        AbstractMod innerMod;
        IModSettings iModSettings;
        string logstr = "preloader : ";
        string preloaderJsonName = "preloader.json";
        bool initialized;
        bool broken;
        bool workshopDataInitialized;
        bool localDataInitialized;
        Dictionary<ulong, PublishedFileId_t> subbedWorkshopItems;
        Dictionary<ulong, string> localItems;
        Guid? _tempGuid;
        Dictionary<string, object> descriptorJson;
        Descriptor descriptor;
        string modPath;

        private Guid tempGuid
        {
            get
            {
                if (_tempGuid == null)
                    _tempGuid = Guid.NewGuid();
                return _tempGuid.Value;
            }
        }

        private class Dependency
        {
            public ulong workshopItemId;
            public string[] assemblyDependencies;

            public Dependency(ulong workshopId, params string[] assembliesToLoad)
            {
                this.workshopItemId = workshopId;
                this.assemblyDependencies = assembliesToLoad;
            }
        }

        private class Descriptor
        {
            public string ModTypeFullName;
            public List<Dependency> Dependencies = new List<Dependency>();
        }

        private void Preload(ModManager.ModEntry entry, string path)
        {
            string descriptorPath = Path.Combine(modPath, preloaderJsonName);

            if (!LoadDescriptor(descriptorPath))
            {
                Debug.LogError(logstr + $"Failed to read {preloaderJsonName} ");
                broken = true;
                return;
            }

            descriptor = ParseDescriptor();
            if (descriptor == null)
            {
                Debug.LogError(logstr + $"Failed parse {preloaderJsonName} ");
                broken = true;
                return;
            }

            if (broken)
            {
                Debug.LogError($"Preloader failed to load the mod, at: { modPath }  read logs.");
                return;
            }

            if (!LoadDependencies())
            {
                broken = true;
                return;
            }

            if (!LoadInnerMod())
            {
                broken = true;
                return;
            }

            Debug.Log($"Preloader SUBSTITUTING MOD: { modPath }");
            SubstituteInnerMod(entry);

            initialized = true;
        }

        private bool LoadDescriptor(string path)
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                descriptorJson = (Dictionary<string, object>)MiniJSON.Json.Deserialize(json);

                if (descriptorJson == null)
                    return false;

                return true;
            }
            else
                return false;
        }

        private Descriptor ParseDescriptor()
        {
            Descriptor descr = new Descriptor();
            descr.ModTypeFullName = descriptorJson[nameof(Descriptor.ModTypeFullName)].ToString();
            List<object> depList = descriptorJson[nameof(Descriptor.Dependencies)] as List<object>;

            if (depList != null)
            {
                foreach (var item in depList)
                {
                    ulong id = 0;

                    var depObj = item as Dictionary<string, object>;
                    if (depObj != null)
                    {
                        id = Convert.ToUInt64(depObj["workshopId"]);
                        List<object> assemblyList = depObj["assembliesToLoad"] as List<object>;

                        if (assemblyList != null)
                        {
                            string[] asmb = new string[assemblyList.Count];
                            for (int i = 0; i < asmb.Length; i++)
                            {
                                asmb[i] = assemblyList[i] as string;
                            }

                            Dependency dep = new Dependency(id, asmb);
                            if (dep != null)
                            {
                                descr.Dependencies.Add(dep);
                            }
                        }
                    }
                }
            }

            return descr;
        }

        private bool LoadDependencies()
        {
            if (descriptor.Dependencies == null || descriptor.Dependencies.Count == 0)
            {
                Debug.Log(logstr + $"No dependencies returned.");
                return true;
            }

            HashSet<string> assembliesSet = new HashSet<string>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                assembliesSet.Add(assemblies[i].GetName().Name);
            }

            for (int i = descriptor.Dependencies.Count - 1; i >= 0; i--)
            {
                var dep = descriptor.Dependencies[i];
                for (int t = 0; t < dep.assemblyDependencies.Length; t++)
                {
                    var assemblyDep = dep.assemblyDependencies[t];
                    string noext = Path.GetFileNameWithoutExtension(assemblyDep);
                    Debug.Log(logstr + $"Trying to find:  " + noext);

                    if (!assembliesSet.Contains(noext))
                    {
                        if (!TryLoadAssemblyLocal(dep.workshopItemId, assemblyDep))
                        {
                            if (!TryLoadAssemblyWorkshop(dep.workshopItemId, assemblyDep))
                            {
                                Debug.LogError(logstr + $"FAILED TO LOAD DEPENDENCY:  " + assemblyDep);
                                return false;
                            }
                        }
                    }
                    else
                    {
                        Debug.Log(logstr + $"{noext} Already loaded.");
                    }
                }
            }

            return true;
        }

        private bool TryLoadAssemblyLocal(ulong wid, string asmb)
        {
            InitializeLocalData();

            if (localItems.TryGetValue(wid, out string path))
            {
                string asmpath = Path.Combine(path, asmb);
                if (File.Exists(asmpath))
                {
                    Assembly.LoadFile(asmpath);
                    Debug.Log(logstr + $"Loaded assembly {asmb} at path:  " + path);
                    return true;
                }
            }

            return false;
        }

        private void InitializeLocalData()
        {
            if (localDataInitialized)
                return;

            localItems = new Dictionary<ulong, string>();
            List<string> localDirs = new List<string>();
            localDirs.Add(GameController.modsPath);
            localDirs.AddRange(Directory.GetDirectories(GameController.modsPath));
            foreach (string path in localDirs)
            {
                string p = Path.Combine(path, "steam_workshop-id");
                if (File.Exists(p))
                {
                    ulong id = Convert.ToUInt64(File.ReadAllText(p));
                    localItems.Add(id, path);
                }
                else
                {
                    Debug.Log(logstr + $"Local path without id:  " + path);
                }
            }

            localDataInitialized = true;
        }


        private bool TryLoadAssemblyWorkshop(ulong wid, string asmb)
        {
            InitializeWorkshopData();

            if (subbedWorkshopItems.TryGetValue(wid, out PublishedFileId_t pf))
            {
                if (SteamUGC.GetItemInstallInfo(pf, out ulong size, out string path, 1024U, out uint timestamp))
                {
                    if (path != null)
                    {
                        string asmpath = Path.Combine(path, asmb);
                        if (File.Exists(asmpath))
                        {
                            Assembly.LoadFile(asmpath);
                            Debug.Log(logstr + $"Loaded assembly {asmb} at path:  " + path);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void InitializeWorkshopData()
        {
            if (workshopDataInitialized)
                return;

            uint count = SteamUGC.GetNumSubscribedItems();
            PublishedFileId_t[] subscribed = new PublishedFileId_t[count];
            uint subscribedItems = SteamUGC.GetSubscribedItems(subscribed, count);
            subbedWorkshopItems = new Dictionary<ulong, PublishedFileId_t>();
            for (uint i = 0U; i < subscribedItems; i += 1U)
            {
                subbedWorkshopItems.Add(subscribed[i].m_PublishedFileId, subscribed[i]);
            }

            workshopDataInitialized = true;
        }

        private bool LoadInnerMod()
        {
            Debug.Log(logstr + $"Looking for assemblies in " + modPath);
            string[] files = Directory.GetFiles(modPath, "*.dll", SearchOption.TopDirectoryOnly);
            Debug.Log(logstr + $"Found {files.Length} dlls");

            Type innerModType = null;
            string thisAssembly = Assembly.GetExecutingAssembly().GetName().Name;

            for (int i = 1; i < files.Length; i++)
            {
                var f = files[i];
                if (f.EndsWith(".dll") && !(Path.GetFileNameWithoutExtension(f) == thisAssembly))
                {
                    Debug.Log(logstr + $"Loading assembly at {f}");
                    var asmb = Assembly.LoadFile(f);

                    Debug.Log(logstr + $"Checking for {descriptor.ModTypeFullName} in {asmb.GetName().Name}");

                    if (innerModType == null)
                    {
                        foreach (Type type in asmb.GetExportedTypes())
                        {
                            if (type.FullName == descriptor.ModTypeFullName)
                            {
                                innerModType = type;
                            }
                        }
                    }
                }
            }

            if (innerModType == null)
            {
                Debug.Log(logstr + $"Inner Mod Type not found, brobably wrong name?");
                return false;
            }
            else
            {
                Debug.Log(logstr + $"Instantiating inner mod : {innerModType.FullName}");
                if (!InstantiateInnerMod(innerModType))
                    return false;
            }

            return true;
        }

        private void SubstituteInnerMod(ModManager.ModEntry entry)
        {
            Type type = entry.GetType();

            var flags = BindingFlags.Public | BindingFlags.Instance;

            PropertyInfo propMod = type.GetProperty("mod", flags);
            propMod.SetValue(entry, innerMod);

            PropertyInfo propOrder = type.GetProperty("orderPriority", flags);
            propOrder.SetValue(entry, innerMod.getOrderPriority());
        }

        private bool InstantiateInnerMod(Type type)
        {
            innerMod = Activator.CreateInstance(type) as AbstractMod;

            if (innerMod == null)
            {
                Debug.Log(logstr + $"Failed to instantiate inner mod ");
                return false;
            }

            iModSettings = innerMod as IModSettings;
            if (iModSettings == null)
                Debug.Log(logstr + $"Mod has no IModSettings");

            return true;
        }
    }
}