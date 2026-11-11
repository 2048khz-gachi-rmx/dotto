FROM python:3.13-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
# Basics an assistant reaches for: file inspection, process/connection view, archives, DNS and
# connectivity probes, and a pager. Deliberately no compilers or heavy media suites.
RUN apt-get update && apt-get install -y --no-install-recommends \
        curl jq ca-certificates \
        file less tree bc procps lsof time bsdmainutils xxd \
        zip unzip xz-utils bzip2 zstd \
        bind9-dnsutils iputils-ping netcat-openbsd iproute2 whois wget \
        ncal git fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

# Charting an assistant can actually use: matplotlib needs numpy and pill, and ships DejaVu
# itself; Pillow can use the DejaVu files installed above for text. Versions pinned so the
# image does not drift between builds.
RUN pip install --no-cache-dir \
        "matplotlib==3.11.2" "numpy==2.5.4" "pillow==13.0.0" \
    && find / -name '__pycache__' -prune -exec rm -rf {} + 2>/dev/null || true
ENV MPLCONFIGDIR=/tmp/matplotlib

ADD https://github.com/eugeneware/ffmpeg-static/releases/download/b6.0/ffmpeg-linux-x64 /usr/local/bin/ffmpeg
ADD https://github.com/eugeneware/ffmpeg-static/releases/download/b6.0/ffprobe-linux-x64 /usr/local/bin/ffprobe
RUN chmod a+rx /usr/local/bin/ffmpeg /usr/local/bin/ffprobe

COPY server.py /server.py
RUN addgroup --system dotto && adduser --system --ingroup dotto dotto
USER dotto
WORKDIR /work
EXPOSE 8080
CMD ["python3", "/server.py"]
