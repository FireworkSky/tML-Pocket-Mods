# Embedded MobileJoystick source

Source supplied by the project owner from their local `Terraria/tModLoader/ModSources/MobileJoystick` directory on 2026-11-16. Version 0.1.4, author yu, client side. Root C# sources, Assets, Localization, icons, descriptions and original README are preserved. Development screenshots/docs, tools, Git metadata and generated bin/obj were excluded. The original source directory is unchanged.

The launcher adds its own project file and build script. The bundled build targets .NET 10 to match the Android CoreCLR and existing Android MonoMod binaries, using precompiled tML/FNA references. tML's normal packager processes the assets with isolated build user data. This is a launcher-specific client build; it is not a desktop mod distribution.

The namespace MobileJoystick is intentionally retained as the vendored mod identity and exempted from the launcher namespace-directory rule. The original README refers to documentation in the source project which is not mirrored here. Asset provenance notices are retained under Assets. No standalone LICENSE was present in the user-supplied mod sources; no new upstream license is asserted here.

Runtime: SDL -> FNA -> bundled touch adapter -> MobileJoystick. The mod owns dual joysticks, touch inventory/menu/map and its in-game layout editor. The launcher's old Android control overlay has been removed. Installation updates preserve mod configurations and back up differing existing TMOD copies.
