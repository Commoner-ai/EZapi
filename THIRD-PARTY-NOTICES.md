# External dependencies and references

- Probably Stolen and Unity/IL2CPP assemblies belong to their owners. Supply them from your own installation; no game/runtime assemblies are distributed here.
- MelonLoader, Il2CppInterop and Harmony are external prerequisites. Install appropriate versions separately; their licenses are not replaced by EZapi's MIT license.
- The metadata inspector uses Mono.Cecil 0.11.6 from NuGet (MIT): https://github.com/jbevain/cecil. It restores during development; the game runtime does not depend on it.
- Official docs: https://github.com/dragonpan2/probably-stolen-modding at commit f2137eb7bebeecfadca1ccabb0cae3757ea4319f. They can be fetched locally but are not republished under EZapi's license.
- Forge is a public functionality reference. EZapi has independent implementation and does not distribute or depend on Forge DLLs. Mappings do not promise ABI compatibility.

EZapi implementation, tests, documentation, examples and automation helpers are published under the MIT LICENSE. Third-party dependencies and references retain their own licenses. Historical verification records are scoped evidence, not game assets or a license to third-party content.

Original game artwork references in docs/art-reference/ (including enlarged images, boards and measured palettes) retain the original creators’ rights and are explicitly excluded from EZapi’s MIT license. See docs/art-reference/NOTICE.md; inclusion does not grant a separate artwork reuse or redistribution license.
