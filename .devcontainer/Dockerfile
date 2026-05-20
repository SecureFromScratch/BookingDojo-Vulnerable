FROM ubuntu:22.04
RUN apt-get update && apt-get install -y --no-install-recommends sudo \
    && useradd -m -s /bin/bash vscode \
    && echo "vscode ALL=(ALL) NOPASSWD:ALL" >> /etc/sudoers \
    && rm -rf /var/lib/apt/lists/*
