class Clone < Formula
  desc "Organize Git checkouts safely"
  homepage "https://github.com/6a6f6a6f/clone"
  url "https://github.com/6a6f6a6f/clone/releases/download/v1.0.0/clone-1.0.0-source.tar.gz"
  version "1.0.0"
  sha256 "23cf7d2e380c06ba2b678eab15ad781d7d5b1b2acf32d9039c072685b86e2944"
  license "WTFPL"

  bottle do
    root_url "https://github.com/6a6f6a6f/clone/releases/download/v1.0.0"
    sha256 cellar: :any_skip_relocation, arm64_tahoe: "90019be74736a6be937913acbd599c0aa6ffd8fd0988e20dfa7e729e661da600"
  end

  depends_on "dotnet" => :build
  depends_on "git"
  depends_on arch: :arm64
  depends_on macos: :tahoe

  def install
    ENV["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    ENV["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    ENV["NUGET_PACKAGES"] = buildpath/"vendor/nuget"
    system "dotnet", "publish", "Clone.Console/Clone.Console.csproj",
           "-c", "Release", "-r", "osx-arm64", "-p:Version=#{version}",
           "-p:NuGetAudit=false", "-o", "publish"
    bin.install "publish/clone"
    zsh_completion.install "completions/_clone"
  end

  test do
    assert_equal "clone #{version}", shell_output("#{bin}/clone --version").strip
    assert_match "example.com/team/repo", shell_output(
      "#{bin}/clone --dry-run --root #{testpath}/projects --config-dir #{testpath}/config https://example.com/team/repo"
    )
  end
end
