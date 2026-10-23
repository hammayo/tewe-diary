import { setupServer } from "msw/node";

// Tests add handlers per case with server.use(...).
export const server = setupServer();
