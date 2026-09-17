# SeedDemoData

Fills a database with the same demo data on every machine, so the same exams can be tried on the
Linux desktop and on the Windows laptop.

```bash
cd SecureExamIDE/WebApi && docker compose up -d      # the stack must be running
dotnet run --project ../tools/SeedDemoData
```

It creates, through the public API and nothing else:

- **demo.professor@example.com** and **demo.student@example.com**, both with the password
  `Password123!`, verified by reading their codes out of Mailpit.
- **Algorithms - September exam** — a text task, a Windows and a Linux toolchain (2 MiB each, not
  real compilers) and a toolchain marked `Any`.
- **Operating Systems - January exam** — a PDF task and a text one, with the two toolchains.
- One **sitting per exam**, starting five minutes ago, whose **one-time code is printed**. The server
  shows a code once and never again, so what the tool prints is the only copy.

Running it again reuses the accounts and exams and only adds a fresh sitting, which is how you get a
new code to test with.

Options: `--api`, `--mailpit`, `--professor-code` (otherwise read from `WebApi/.env`), and
`--dump <folder>`, which writes the generated files out without touching the server.

To start from an empty database, note that `docker compose down -v` does **not** wipe this stack: the
data is in bind mounts under `WebApi/.containers`. Remove them through a container, which avoids
needing `sudo` for the root-owned folders:

```bash
docker compose down
docker run --rm -v "$PWD/.containers:/containers" alpine rm -rf /containers/db /containers/minio
docker compose up -d --build
```

There is no SQL seeding, and there cannot be: task files and toolchains are objects in MinIO, and a
sitting's package is sealed by the API at the moment the sitting is created.
