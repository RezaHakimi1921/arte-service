import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App, { applyTheme } from "./App";
import "./styles.css";

// Light is the default: readable in a bright workshop. Dark stays one tap away in settings.
let saved = "light";
try {
  saved = localStorage.getItem("arte-theme") ?? "light";
} catch {
  /* storage may be blocked */
}
applyTheme(saved === "dark" ? "dark" : "light", false);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
