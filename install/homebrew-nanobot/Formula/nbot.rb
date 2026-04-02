class Nanobot < Formula
  desc "A lightweight personal AI assistant framework (.NET)"
  homepage "https://github.com/mbzcnet/NanoBot.Net"
  version "0.1.4"
  license "MIT"

  on_macos do
    on_intel do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v0.1.4/nbot-osx-x64.tar.gz"
      sha256 "TODO"
    end
    on_arm do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v0.1.4/nbot-osx-arm64.tar.gz"
      sha256 "TODO"
    end
  end

  on_linux do
    on_intel do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v0.1.4/nbot-linux-x64.tar.gz"
      sha256 "TODO"
    end
    on_arm do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v0.1.4/nbot-linux-arm64.tar.gz"
      sha256 "TODO"
    end
  end

  def install
    bin.install "nbot"
    pkgshare.install "workspace"
  end

  test do
    assert_match "NanoBot", shell_output("#{bin}/nbot --version")
  end
end
