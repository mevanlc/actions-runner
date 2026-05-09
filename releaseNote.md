```powershell
# Windows x64
mkdir \actions-runner ; cd \actions-runner
Invoke-WebRequest -Uri https://github.com/<OWNER_REPO>/releases/download/v<RUNNER_VERSION>/actions-runner-win-x64-<RUNNER_VERSION>.zip -OutFile actions-runner-win-x64-<RUNNER_VERSION>.zip
Add-Type -AssemblyName System.IO.Compression.FileSystem ;
[System.IO.Compression.ZipFile]::ExtractToDirectory("$PWD\actions-runner-win-x64-<RUNNER_VERSION>.zip", "$PWD")
```

```powershell
# Windows arm64
mkdir \actions-runner ; cd \actions-runner
Invoke-WebRequest -Uri https://github.com/<OWNER_REPO>/releases/download/v<RUNNER_VERSION>/actions-runner-win-arm64-<RUNNER_VERSION>.zip -OutFile actions-runner-win-arm64-<RUNNER_VERSION>.zip
Add-Type -AssemblyName System.IO.Compression.FileSystem ;
[System.IO.Compression.ZipFile]::ExtractToDirectory("$PWD\actions-runner-win-arm64-<RUNNER_VERSION>.zip", "$PWD")
```

```bash
# OSX x64
mkdir actions-runner && cd actions-runner
curl -O -L https://github.com/<OWNER_REPO>/releases/download/v<RUNNER_VERSION>/actions-runner-osx-x64-<RUNNER_VERSION>.tar.gz
tar xzf ./actions-runner-osx-x64-<RUNNER_VERSION>.tar.gz
```

```bash
# OSX arm64 (Apple silicon)
mkdir actions-runner && cd actions-runner
curl -O -L https://github.com/<OWNER_REPO>/releases/download/v<RUNNER_VERSION>/actions-runner-osx-arm64-<RUNNER_VERSION>.tar.gz
tar xzf ./actions-runner-osx-arm64-<RUNNER_VERSION>.tar.gz
```

```bash
# Linux x64
mkdir actions-runner && cd actions-runner
curl -O -L https://github.com/<OWNER_REPO>/releases/download/v<RUNNER_VERSION>/actions-runner-linux-x64-<RUNNER_VERSION>.tar.gz
tar xzf ./actions-runner-linux-x64-<RUNNER_VERSION>.tar.gz
```

```bash
# Linux arm64
mkdir actions-runner && cd actions-runner
curl -O -L https://github.com/<OWNER_REPO>/releases/download/v<RUNNER_VERSION>/actions-runner-linux-arm64-<RUNNER_VERSION>.tar.gz
tar xzf ./actions-runner-linux-arm64-<RUNNER_VERSION>.tar.gz
```

```bash
# Linux arm
mkdir actions-runner && cd actions-runner
curl -O -L https://github.com/<OWNER_REPO>/releases/download/v<RUNNER_VERSION>/actions-runner-linux-arm-<RUNNER_VERSION>.tar.gz
tar xzf ./actions-runner-linux-arm-<RUNNER_VERSION>.tar.gz
```
