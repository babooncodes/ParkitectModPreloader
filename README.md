# ParkitectModPreloader
Allows you to have a dependency on other mods dll. Will load those before it loads your mod.


# Usage 

1. Copy `0Preloader.dll` into the folder with your mod,
2. Change `preloader.json` contents to:
    - point to correct mod type (your own mod inheriting AbstractMod, full type with namespaces) 
    - dependencies that the mod requires, workshop id of the mod containing them, and names of the dlls.
3. Make sure your own mod dll is not the same as folder name, otherwise Parkitect will load it first. You can rename it like `zMyMod` and it will work.

# Known Issues
- Fails to get the list of workshop mods if steam is in offline mode.

# How its done

Preloader uses a hack where by counting how many times the `getIdentifier` is called on a mod where it is created by Parki,
it can identify the point when `ModEntry` is already constructed. At that point it initializes.

It is necessary because there is no way to get the path to the folder the preloader is executing it, as once the assembly was loaded
successive loads will return the same assembly, and it will point to the same path the first preloader was pointing.
So preloader catches the moment the mod was given a path, but just before it is inserted into important stuff.

After the path is known, preloader looks for `preloader.json` in that folder and goes through the list of dependencies defined there.
It looks through both local mod folder in MyDocuments and through workshop mods, it loads ahead of time those dlls that werent already loaded.

When all dependencies are loaded, it searches for the class of the actual mod in assemblies inside its folder and instantiates it.
Then it substitutes itself for the actual mod in the ModEntry, without affecting anything in the game.
When its done you have your actual mod loaded there and the preloader stub mod is discarded.

# Building

Few dependencies
- com.rlabrecque.steamworks.net   (steamworks, in Parki Managed)
- UnityEngine
- UnityEngine.CoreModule
- Parkitect
