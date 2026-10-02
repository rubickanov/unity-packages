# Known issues

Found in a review on 2026-10-02; not fixed, since no game uses the package. Fix before adopting it.

- **Every config handle leaks.** `AddressablesAssetLoader.LoadAsync` returns the typed `AsyncOperationHandle<T>`
  boxed as the release token (`AddressablesAssetLoader.cs:32`), and `Release` (`:37`) checks for the untyped
  `AsyncOperationHandle`; a boxed struct of another type never matches, so `Release`, `ReleaseAll` and `Dispose`
  release nothing. The tests use only a fake loader (`ConfigServiceTests.cs:293`). Return the handle converted to
  the untyped `AsyncOperationHandle`, and add a test with the real loader.
- **`ConfigDatabase` throws a NullReferenceException on an empty list slot** (`ConfigDatabase.cs:51`, `:104`).
- **Two logging paths.** `ConfigDatabase` logs through `Debug.LogError` (`:81`) and the service through
  Microsoft.Extensions.Logging, a DLL the package does not declare and no game has. Move both to
  `com.rubickanov.log`.
- **Addressables are required** although the loader is meant to be swappable. Move `AddressablesAssetLoader` into an
  optional assembly so a Resources loader works without Addressables; the games load configs through Resources.
