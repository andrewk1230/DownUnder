# Repository Guidelines

## Project Structure & Module Organization

This is a Unity project. `Assets/Scripts/` holds project gameplay and camera scripts, `Assets/Scenes/` holds scenes, `Assets/Editor/` contains editor utilities, `Packages/` declares dependencies, and `ProjectSettings/` records editor settings. `HANDOFF.md` documents known project behavior; many other `Assets/` folders are third-party content.

## Build, Test, and Development Commands

Open the project folder in Unity Editor 6000.5.0f1, the version recorded in `ProjectSettings/ProjectVersion.txt`. Let imports and compilation finish, then open the affected scene and enter Play mode. There is no tracked command-line build or test script.

## Coding Style & Naming Conventions

Use four-space indentation and standard C# PascalCase type/member names with camelCase local variables. Place new gameplay code in `Assets/Scripts/`; keep editor-only helpers in `Assets/Editor/`. Preserve Unity `.meta` files and serialized references. Avoid direct YAML edits to scenes or prefabs; use the Editor.

## Testing Guidelines

No first-party automated Unity test assembly or coverage target is tracked. For scene or controller changes, verify compilation, Play mode behavior, both relevant camera modes, and console errors. Record the scene, input steps, and observed result in the PR.

## Commit & Pull Request Guidelines

Recent subjects describe concrete Unity changes, including movement and camera work. Continue with concise imperative subjects. PRs should list scenes and assets touched, Play mode checks, screenshots or a short capture for visual changes, and any dependency changes.

## Security & Configuration Notes

Do not commit `Library/`, `Logs/`, generated app bundles, or local user settings. Review `HANDOFF.md` before changing input, animation, or cameras.
