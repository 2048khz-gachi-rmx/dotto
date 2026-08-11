FROM python:3.13-slim
RUN apt-get update && apt-get install -y curl jq && rm -rf /var/lib/apt/lists/*

# Static ffmpeg build — avoids pulling in the entire Debian ffmpeg dependency tree (GUI libs, systemd, etc.)
ADD https://github.com/eugeneware/ffmpeg-static/releases/download/b6.0/ffmpeg-linux-x64 /usr/local/bin/ffmpeg
ADD https://github.com/eugeneware/ffmpeg-static/releases/download/b6.0/ffprobe-linux-x64 /usr/local/bin/ffprobe
RUN chmod a+rx /usr/local/bin/ffmpeg /usr/local/bin/ffprobe

COPY server.py /server.py
RUN addgroup --system dotto && adduser --system --ingroup dotto dotto
USER dotto
WORKDIR /work
EXPOSE 8080
CMD ["python3", "/server.py"]
