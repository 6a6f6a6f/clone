SOLUTION := Clone.sln
PROJECT := Clone.Console/Clone.Console.csproj
DOTNET ?= dotnet
RID ?= osx-arm64
export CLONE_BUILD_RID := $(RID)

.PHONY: all restore build publish clean test format check package-dev bottle-dev packaging-check
all: build
restore:
	$(DOTNET) restore $(SOLUTION)
build:
	$(DOTNET) build $(SOLUTION) -c Release
publish:
	$(DOTNET) publish $(PROJECT) -c Release -r "$$CLONE_BUILD_RID" -o "artifacts/publish/$$CLONE_BUILD_RID"
clean:
	$(DOTNET) clean $(SOLUTION)
test:
	$(DOTNET) test $(SOLUTION) -c Release
format:
	$(DOTNET) format $(SOLUTION)
check:
	$(DOTNET) format $(SOLUTION) --verify-no-changes --severity error
	$(DOTNET) build $(SOLUTION) -c Release --no-restore
	$(DOTNET) test $(SOLUTION) -c Release --no-build
	python3 scripts/test_release.py

VERSION ?= 0.2.0
export CLONE_BUILD_VERSION := $(VERSION)
package-dev:
	python3 scripts/release.py prepare --version "$$CLONE_BUILD_VERSION" --rid "$$CLONE_BUILD_RID" --development
packaging-check:
	python3 scripts/test_release.py
	shellcheck packaging/macos/preinstall packaging/macos/uninstall.sh
	actionlint -ignore 'constant expression "false" in condition' .github/workflows/*.yml
	zsh -n completions/_clone

bottle-dev:
	python3 scripts/homebrew.py prepare --version "$$CLONE_BUILD_VERSION" --development
