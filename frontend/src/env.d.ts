/// <reference types="astro/client" />

declare namespace App {
  interface Locals {
    cfContext?: unknown;
    runtime?: {
      env?: Record<string, string | undefined>;
    };
  }
}
