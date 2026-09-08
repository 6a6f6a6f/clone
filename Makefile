SOLUTION := Clone.sln
PROJECT := Clone.Console/Clone.Console.csproj
DOTNET ?= dotnet
RID ?= osx-arm64

.PHONY: all restore build publish clean test format check
all: build
restore:
	$(DOTNET) restore $(SOLUTION)
build:
	$(DOTNET) build $(SOLUTION) -c Release
publish:
	$(DOTNET) publish $(PROJECT) -c Release -r $(RID) -o artifacts/publish/$(RID)
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
