# SecureExamIDE

A desktop app for taking **programming exams on students' own laptops**, securely and without an internet connection on exam day.

This is a graduation thesis project by Aleksa Mitić.

## The idea

Programming exams usually need a lab full of faculty computers and a reliable network. SecureExamIDE removes both. Students use their own laptops, and the exam itself runs fully offline.

**How an exam goes:**

1. **Register once.** The student installs the app and registers. This binds the laptop to their account, so they never have to log in again to hand in work.
2. **Download ahead of time.** At home, the student downloads the exam package and the tools it needs, such as compilers and libraries. The package is **encrypted**, so nobody can read the tasks early.
3. **Unlock at the exam.** When the exam starts, the professor reads out a **one-time code**. The code unlocks the package directly on the laptop, with no internet needed.
4. **Work offline.** The student writes code in a fullscreen editor that looks and feels like VS Code. Pasting from outside the app is blocked, and activity is logged.
5. **Hand it in.** The finished solution is sealed on the laptop so it can't be changed afterwards. It's uploaded automatically once the laptop is back online.

The professor's side mirrors this. They create an exam, attach task files and tools, publish it, schedule a sitting (which produces that sitting's code), and collect the submissions.

## Security

- **Tasks stay secret until the exam starts.** The package is encrypted. Only the one-time code opens it, and the server never stores that code.
- **Each sitting has its own code.** A code leaked after the January sitting can't open the September retake.
- **No passwords on exam day.** Registering gives the laptop a device key, which it uses to submit. A lost laptop can be removed from the account.
- **Submissions can't be changed.** Each student can hand in only once per sitting. The server records a fingerprint of the solution and which laptop sent it.

## Project status

| Part | Status |
|---|---|
| **Web API** (server) | Mostly done. Registration, publishing exams, encrypted packages and handing in solutions all work. Next: professors collecting submissions, and activity logs |
| **Desktop client** | Not started yet. Planned for Windows first, then Linux and macOS |

Automatic grading, an analytics dashboard for professors, and integration with the faculty's student system are out of scope for now.

## Built with

- **.NET 10** and **ASP.NET Core** for the Web API
- **Avalonia** for the cross-platform desktop client
- **PostgreSQL** for the data, **MinIO** for file storage
- **Docker** to run everything locally

## Repository layout

```
SecureExamIDE/
├── WebApi/    the server: API, database, storage and tests
└── Client/    the desktop app
```

## Running it locally

You need [Docker](https://www.docker.com/) installed.

```bash
cd WebApi
cp .env.example .env      # then open .env and fill in your own values
docker compose up -d --build
```

Once it's running:

- **API documentation** (lets you try every endpoint): http://localhost:5000/scalar/v1
- **File storage console:** http://localhost:9001

To stop everything, run `docker compose down`.

To run the tests (they need Docker too), run `dotnet test` from `WebApi/`. This needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).
