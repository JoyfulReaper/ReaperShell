# Smoke test for hook management.
ritual new hook-smoke
hook events
hook add startup hook-smoke
hook list
hook remove startup hook-smoke
hook clear startup
hook list
status
