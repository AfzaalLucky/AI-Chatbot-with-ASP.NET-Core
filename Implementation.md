Build a production-ready AI chatbot application using **ASP.NET Core**.

### Objective

Create a modern, scalable chatbot where users can send messages and receive AI-generated responses through an ASP.NET Core backend.

### Technology Stack

- ASP.NET Core Web API
- C#
- .NET 8 or the latest stable .NET version
- Entity Framework Core
- SQL Server
- REST API
- SignalR for real-time chat updates
- A modern frontend such as React, Blazor, or Razor Pages
- An AI provider API with configurable API credentials
- Dependency Injection and the Options pattern

### Core Features

1. **Chat Interface**

   - Modern, responsive chat UI.
   - Display user and AI messages separately.
   - Show timestamps.
   - Show a loading/typing indicator while the AI is responding.
   - Allow users to start a new conversation.
   - Preserve conversation history.

2. **AI Integration**

   - Create a clean abstraction such as `IAiChatService`.
   - Implement the AI provider behind this abstraction.
   - Keep the provider/API key configurable through `appsettings.json` and environment variables.
   - Never expose API keys to the frontend.
   - Support conversation context so the AI can use previous messages when generating responses.

3. **Backend API**
    Create well-structured endpoints such as:

   - `POST /api/chat`
   - `GET /api/conversations`
   - `GET /api/conversations/{id}`
   - `POST /api/conversations`
   - `DELETE /api/conversations/{id}`

4. **Database**
    Create entities for:

   - User
   - Conversation
   - Message

   Store:

   - Conversation ID
   - User/assistant role
   - Message content
   - Created timestamp
   - Relevant metadata

   Configure Entity Framework Core migrations and database relationships.

5. **Authentication**

   - Add ASP.NET Core Identity or JWT authentication.
   - Users should only be able to access their own conversations.
   - Protect all private chat endpoints.

6. **Real-Time Responses**

   - Use SignalR where appropriate.
   - Display AI responses progressively if the selected AI provider supports streaming.
   - Handle connection failures and reconnects gracefully.

### Architecture

Use a clean, maintainable architecture such as:

```text
/src
  /Chatbot.Api
  /Chatbot.Application
  /Chatbot.Domain
  /Chatbot.Infrastructure
  /Chatbot.Web
/tests
  /Chatbot.UnitTests
  /Chatbot.IntegrationTests
```

Follow SOLID principles and keep business logic out of controllers.

### Security

Implement:

- Input validation
- Authentication and authorization
- Rate limiting
- Secure API-key storage
- Proper CORS configuration
- Protection against excessively large requests
- Logging without exposing secrets or sensitive conversation data
- Global exception handling
- HTTPS

### API Design

Use DTOs rather than exposing database entities directly.

Return appropriate HTTP status codes and consistent error responses, preferably using the Problem Details standard.

### Frontend

Build a polished chatbot interface with:

- Sidebar containing conversations
- New conversation button
- Chat message area
- Message input
- Send button
- Loading/typing indicator
- Markdown rendering for AI responses
- Code-block formatting
- Responsive mobile layout
- Error notifications

### Testing

Include:

- Unit tests for application/business logic
- API integration tests
- Tests for authentication and authorization
- Tests for conversation creation and retrieval
- Tests for AI service failures
- Tests for validation and error handling

Mock the AI provider in automated tests.

### Configuration

Provide:

- `appsettings.json`
- `appsettings.Development.json`
- Environment-variable configuration
- Example configuration using placeholder values
- EF Core migration commands
- Database setup instructions

Never hard-code secrets.

### Developer Experience

Provide:

1. Complete project structure.
2. All important source files with complete code.
3. Database models and EF Core configuration.
4. API controllers/endpoints.
5. AI service implementation.
6. Authentication setup.
7. SignalR implementation.
8. Frontend implementation.
9. Dependency injection configuration.
10. Exception handling and logging.
11. Unit and integration tests.
12. README with setup and deployment instructions.

### Code Quality

Write clean, production-oriented C# code. Use asynchronous APIs (`async`/`await`) appropriately, cancellation tokens where useful, nullable reference types, dependency injection, configuration binding, and meaningful error handling.

Explain important architectural decisions briefly as you build the application.

Do not provide pseudo-code for core functionality. Provide runnable code that can be copied into a new solution and executed after configuring the required database and AI provider credentials.