# Engineering rules

- Follow the hackathon technical specification. Do not silently weaken a requirement.
- Prefer simple, testable designs over speculative abstractions.
- Keep domain and orchestration code independent from files, HTTP, Windows services, and databases.
- Use meaningful names, small cohesive types, guard clauses, async I/O, and CancellationToken.
- Keep configuration outside code. Never commit real device tokens or other secrets.
- Every bug fix must include a regression test when practical.
- Run `dotnet test` and `dotnet build` before considering a change complete.
- Preserve at-least-once delivery and server-side idempotency through EventId.
