# Agent instructions

- For now, do not create, draft, open, or submit pull requests for this project.
- Do not push changes to `main` (or open pull requests) unless the user explicitly asks to push. Do not push changes to the upstream `MichaelEGA/Open-Galaxy` repository.
- Only change this policy if the user explicitly asks to do so.
- **IMPORTANT**: When creating Unity assets like prefabs, shaders, materials, or scenes, ALWAYS use the Unity MCP tools instead of manually writing YAML files. Use `unityMCP.manage_asset` to create, edit, or import assets properly. Never write raw .prefab files directly with the write tool - this bypasses Unity's asset database and can cause duplicate identifiers and other issues.
- Testing in the editor: whenever possible, use the `GalaxyPlanetTestTool.TestAwingTatooine()` test tool (X-Wing on Tatooine) to validate gameplay instead of manual scene setup.
- Unity verification policy: do NOT verify in Unity (play mode, screenshots, editor checks) unless the user explicitly asks for it — it takes too long. Just implement and compile-check.
