# Smoke test for a generated hello command pack.
repo new generated-smoke
repo build generated-smoke
repo load generated-smoke
hello
repo reload generated-smoke
hello
repo unload generated-smoke
plugins
