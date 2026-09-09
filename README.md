# Clone

Keep your Git repositories organized in `~/Projects/host/owner/repository`.
For macOS 26 (Tahoe) on Apple Silicon.

## Install

```sh
brew tap 6a6f6a6f/clone https://github.com/6a6f6a6f/clone.git
brew install --formula 6a6f6a6f/clone/clone
```

Homebrew installs Git if needed. No .NET runtime is required.

## Usage

```sh
clone https://github.com/owner/repository.git
clone git@github.com:owner/repository.git
```

Choose a different project folder:

```sh
clone config set-root "$HOME/Work"
```

For private repositories, use your existing Git or SSH credentials.
Run `clone --help` for options or `clone doctor` to check your setup.

## Update

```sh
brew update
brew upgrade 6a6f6a6f/clone/clone
```

[Installation details](docs/MACOS_DISTRIBUTION.md) ·
[Report a vulnerability](SECURITY.md) ·
[License](LICENSE)
