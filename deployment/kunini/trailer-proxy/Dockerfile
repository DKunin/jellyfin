FROM python:3.13.18-slim@sha256:2d44738ebf4ce5f63b9d9ea871e9c7cfdc0426f7b0c97cac915fd88ded1d86bb

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir --requirement requirements.txt

COPY trailer_proxy.py test_trailer_proxy.py ./
RUN python -m unittest -q test_trailer_proxy.py \
    && rm test_trailer_proxy.py

EXPOSE 8080 8081
CMD ["python", "/app/trailer_proxy.py"]
