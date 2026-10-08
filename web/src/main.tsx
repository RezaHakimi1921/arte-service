import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App, { applyTheme } from "./App";
import "./styles.css";
import { installErrorReporting, reportError } from "./errors";

installErrorReporting();

// Light is the default: readable in a bright workshop. Dark stays one tap away in settings.
let saved = "light";
try {
  saved = localStorage.getItem("arte-theme") ?? "light";
} catch {
  /* storage may be blocked */
}
applyTheme(saved === "dark" ? "dark" : "light", false);

// React 19 hands render errors here instead of to window.onerror.
createRoot(document.getElementById("root")!, {
  onUncaughtError: (error) => reportError(error),
}).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
