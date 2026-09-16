# ADR-0010 — Provider SDKs are infrastructure adapters

**Status: Accepted**

## Decision

WinUI/Compose views, ViewModels and domain/application logic do not call Supabase (or another cloud provider) SDK directly.

Repositories/remote data sources hide provider-specific auth, REST, Storage and Function APIs behind application contracts.

## Why

The current Kotlin/C# Supabase clients are provider/SDK implementation choices, not durable domain contracts. Isolation improves testing, replacement and error normalization.