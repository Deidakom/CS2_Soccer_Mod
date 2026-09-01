FROM python:3.12-alpine

WORKDIR /app
COPY auth_server.py /app/auth_server.py

USER nobody
EXPOSE 8080

CMD ["python", "/app/auth_server.py"]
