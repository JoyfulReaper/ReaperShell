# Smoke test for cross-language command pack support.
repo add multilang-smoke ./sample-packs/multi-language-pack
repo trust multilang-smoke
command list multilang-smoke
repo build multilang-smoke
repo load multilang-smoke
hello-csharp
hello-fsharp
hello-vb
plugins
repo unload multilang-smoke
plugins
