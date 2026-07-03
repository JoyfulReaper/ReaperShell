# Smoke test for command forge.
repo new forge-smoke
command templates
command list forge-smoke
command new forge-smoke test-basic
command new forge-smoke test-file --template file
command new forge-smoke test-process --template process
command list forge-smoke
repo build forge-smoke
repo load forge-smoke
test-basic
test-file README.md
test-process dotnet --version
repo unload forge-smoke
repo remove forge-smoke
