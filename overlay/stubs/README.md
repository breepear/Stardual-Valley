Compile-only stubs. They describe just the upstream app and AndroidX methods that
`StardualActivity` calls, so it can be compiled without the upstream sources.
They are not packaged into the app: at runtime the real classes inside the
upstream APK are used.
