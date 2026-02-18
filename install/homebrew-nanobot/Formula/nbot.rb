class Nbot < Formula
  desc "A lightweight personal AI assistant framework (.NET)"
  homepage "https://github.com/mbzcnet/NanoBot.Net"
  version "0.1.0"
  license "MIT"

  on_macos do
    on_intel do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v#{version}/nbot-osx-x64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
    on_arm do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v#{version}/nbot-osx-arm64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
  end

  on_linux do
    on_intel do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v#{version}/nbot-linux-x64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
    on_arm do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v#{version}/nbot-linux-arm64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
  end

  def install
    bin.install "nbot"
    pkgshare.install "workspace"
  end

  test do
    assert_match "nbot", shell_output("#{bin}/nbot --version")
  end
end
