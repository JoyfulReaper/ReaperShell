# Smoke test for the sample hello command pack.
repo add sample-smoke ./sample-packs/hello-pack
repo trust sample-smoke
repo build sample-smoke
repo load sample-smoke
hello
plugins
repo reload sample-smoke
hello
repo unload sample-smoke
plugins
