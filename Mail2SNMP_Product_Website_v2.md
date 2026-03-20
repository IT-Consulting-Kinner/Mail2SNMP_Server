# Mail2SNMP

## Convert system emails into structured monitoring events --- reliably and securely.

Turn incoming system emails into SNMP traps or webhooks without writing
or maintaining custom scripts.

**On-Premise. No cloud. No phone-home. Designed for secure enterprise
environments.**

------------------------------------------------------------------------

# The Problem

Many systems --- especially legacy applications, appliances, and
industrial devices --- send alerts via email only.

At the same time:

-   Your monitoring system must not access IMAP directly\
-   Custom mail-parsing scripts are forbidden or not maintainable\
-   Rule changes must be traceable\
-   Maintenance windows cause alert storms\
-   Security teams require encrypted credential storage

Mail2SNMP closes this gap as a dedicated, supportable mail-to-event
bridge.

------------------------------------------------------------------------

# How It Works

1.  Poll one or more IMAP mailboxes\
2.  Apply structured parsing rules\
3.  Create internal events with lifecycle tracking\
4.  Apply deduplication, rate limits and maintenance windows\
5.  Deliver events via SNMP (v1/v2c/v3) or Webhook (HTTP POST)

No brittle scripts. No hidden cron jobs. No silent failures.

------------------------------------------------------------------------

# Why Not Just a Script?

Scripts usually lack:

-   Structured audit trail (who changed what and when)\
-   Dead-letter handling for failed deliveries\
-   Predictable retry strategies\
-   Secure credential encryption\
-   Maintenance window handling\
-   Clear diagnostics and health endpoints\
-   Operational transparency

Mail2SNMP is designed as an infrastructure component --- not a
workaround.

------------------------------------------------------------------------

# Core Capabilities

-   **SNMP v1 / v2c / v3 (AuthPriv)**\
-   **Webhook delivery with optional HMAC signing**\
-   **Maintenance windows to suppress alerts cleanly**\
-   **Event replay for testing and troubleshooting**\
-   **Audit trail for configuration and login events**\
-   **Secure credential storage (AES-256-GCM)**

------------------------------------------------------------------------

# Security & Operations

## Security

-   AES-256-GCM encrypted credentials\
-   Master key stored separately with restricted ACL\
-   No plaintext secrets in database or logs\
-   No outbound telemetry\
-   Optional SSO via OIDC (e.g. ADFS / Azure AD)

## Operations

-   Dead-letter handling for failed webhooks (Enterprise)\
-   Health endpoints (/health/live, /health/ready)\
-   Structured logging (Serilog)\
-   Optional Prometheus metrics\
-   Controlled rate limits and flood protection

------------------------------------------------------------------------

# Technical Overview

  Component       Description
  --------------- --------------------------------------------
  Deployment      On-Premise (MSI + Windows Service)
  Architecture    Single-Tenant
  Database        SQL Server (Production), SQLite (Dev/Demo)
  Web UI          Blazor Server
  API             REST + OpenAPI
  Integrations    SNMP v1/v2c/v3, Webhook (HTTP POST)
  Observability   Health endpoints + optional metrics

------------------------------------------------------------------------

# Editions

## Community Edition

-   Free, closed source\
-   SNMP v1/v2c\
-   Webhook (basic)\
-   Maintenance windows\
-   Event replay\
-   Local users (cookie authentication)\
-   Basic audit trail\
-   Limited mailboxes/jobs/worker instances

## Enterprise Edition

Includes everything from Community plus:

-   SNMP v3 (AuthPriv)\
-   Webhook HMAC signing\
-   OIDC (ADFS / Azure AD / Keycloak)\
-   Full audit trail (IP, UserAgent, CorrelationId)\
-   Dead-letter UI with auto-retry\
-   Diagnostics bundle\
-   Backup CLI\
-   Advanced rate-limit visibility\
-   Unlimited mailboxes, jobs and worker instances

------------------------------------------------------------------------

# Frequently Asked Questions

**Does Mail2SNMP replace my monitoring system?**\
No. It complements existing monitoring platforms by transforming
email-based alerts into structured events.

**Is this a cloud service?**\
No. Mail2SNMP is fully On-Premise.

**What happens if the master key is lost?**\
Encrypted credentials must be re-entered. The system will not start with
a mismatching key.

**Does it support SNMP v3?**\
Yes (Enterprise Edition).

**Can it integrate with ADFS or Azure AD?**\
Yes, via OIDC (Enterprise Edition).

------------------------------------------------------------------------

# Get Started

Request a technical walkthrough or download the architecture overview to
evaluate Mail2SNMP in your environment.

Mail2SNMP --- a secure, supportable mail-to-event bridge for enterprise
On-Prem environments.
