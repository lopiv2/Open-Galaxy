# Agent instructions

- For now, do not create, draft, open, or submit pull requests for this project.
- Always push changes directly to `main` in the user's fork. Do not push changes to the upstream `MichaelEGA/Open-Galaxy` repository.
- Only change this policy if the user explicitly asks to do so.
- **IMPORTANT**: When creating Unity assets like prefabs, shaders, materials, or scenes, ALWAYS use the Unity MCP tools instead of manually writing YAML files. Use `unityMCP.manage_asset` to create, edit, or import assets properly. Never write raw .prefab files directly with the write tool - this bypasses Unity's asset database and can cause duplicate identifiers and other issues.
