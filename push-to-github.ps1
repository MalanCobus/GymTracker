# push-to-github.ps1
# Run this from the folder that contains your solution/project files.

$ErrorActionPreference = "Stop"

$GitHubUser = "MalanCobus"
$RepoHttps  = "https://github.com/MalanCobus/GymTracker.git"
# Include username in the URL so credential caching can differentiate accounts better:
$RepoRemote = "https://$GitHubUser@github.com/MalanCobus/GymTracker.git"

function Require-Command($name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Missing command '$name'. Install it and retry."
    }
}

Require-Command git

Write-Host "Repo remote: $RepoRemote"

# 1) Init git if needed
if (-not (Test-Path ".git")) {
    git init | Out-Null
    Write-Host "Initialized git repo."
}

# 2) Ensure origin points to the right repo
$hasOrigin = $false
try {
    $remotes = git remote
    if ($remotes -match "^origin$") { $hasOrigin = $true }
} catch {}

if ($hasOrigin) {
    git remote set-url origin $RepoRemote
    Write-Host "Updated existing 'origin' remote."
} else {
    git remote add origin $RepoRemote
    Write-Host "Added 'origin' remote."
}

# 3) Make sure we're on main
try { git branch -M main | Out-Null } catch {}

# 4) Make an initial commit if needed / commit current changes
#    (Won't commit if there are no changes.)
$changes = git status --porcelain
if ($changes) {
    git add -A
    try {
        git commit -m "Initial commit" | Out-Null
    } catch {
        # If commit fails because identity isn't set, set a local identity and retry.
        Write-Host "Setting local git author identity (repo-only)."
        git config user.name $GitHubUser
        git config user.email "$GitHubUser@users.noreply.github.com"
        git commit -m "Initial commit" | Out-Null
    }
    Write-Host "Committed changes."
} else {
    Write-Host "No local changes to commit."
}

# 5) If remote main already exists (repo has README/license), pull once to align histories
$remoteMainExists = $false
try {
    $ls = git ls-remote --heads origin main
    if ($ls) { $remoteMainExists = $true }
} catch {}

if ($remoteMainExists) {
    Write-Host "Remote 'main' exists; pulling to avoid push rejection..."
    git pull origin main --allow-unrelated-histories --no-rebase
}

# 6) Clear cached credentials ONLY for github.com + this username (so it re-prompts)
#    This does NOT remove credentials for your other GitHub username.
$gcm = Get-Command "git-credential-manager-core" -ErrorAction SilentlyContinue
if ($gcm) {
    @"
protocol=https
host=github.com
username=$GitHubUser

"@ | git-credential-manager-core erase
    Write-Host "Cleared cached creds for github.com/$GitHubUser (only)."
} else {
    Write-Host "git-credential-manager-core not found; skipping targeted credential erase."
    Write-Host "If you still get 403, you'll need to sign in again when prompted."
}

# 7) Push (this is where Git will prompt / open browser to authenticate)
Write-Host "Pushing to GitHub..."
git push -u origin main

Write-Host "`nDONE. If you were prompted to sign in, use the GitHub account that owns MalanCobus/GymTracker."
Write-Host "If it asks for a password, use a GitHub Personal Access Token (PAT) instead of your GitHub password."
