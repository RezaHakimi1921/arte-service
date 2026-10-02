import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App, { applyTheme } from "./App";
import "./styles.css";

let saved = "dark";
try {
  saved = localStorage.getItem("theme") ?? "dark";
} catch {
  /* storage may be blocked */
}
applyTheme(saved === "light" ? "light" : "dark");

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
