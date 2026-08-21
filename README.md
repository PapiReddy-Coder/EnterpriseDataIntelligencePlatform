# Enterprise Data Intelligence Platform

## Overview

Enterprise Data Intelligence Platform (EDIP) is a .NET 8 Web API
designed to provide a secure, scalable, and centralized platform for
managing workspaces, users, authentication, datasets, metadata, version
history, and data catalog operations. The platform follows a layered
architecture using ASP.NET Core, Entity Framework Core, and SQL Server.
It supports Role-Based Access Control (RBAC), workspace isolation, audit
logging, dataset lifecycle management, metadata versioning, search, and
filtering.

------------------------------------------------------------------------

# Key Features

## Authentication & Security

Features include:

-   JWT Access Token Authentication
-   Refresh Token Rotation
-   Secure Login & Logout
-   Forgot Password / Reset Password
-   Change Password
-   Token Revocation
-   Security Stamp Validation
-   Session Revocation

------------------------------------------------------------------------

## Workspace Management

Capabilities include:

-   Create Workspace
-   Update Workspace
-   Activate / Deactivate Workspace
-   Workspace Isolation
-   Cross-workspace protection

Only authorized users can manage workspace information.

------------------------------------------------------------------------

## User & Role Management

Supported features:

-   Register Users
-   Activate / Deactivate Users
-   Assign Roles
-   Assign Permissions
-   User Search
-   Filtering
-   Pagination

### Predefined Roles

-   Platform Administrator
-   Workspace Administrator
-   Data Analyst
-   Business User
-   Viewer

Dataset authorization is enforced according to permissions, workspace
scope, and ownership rules.

------------------------------------------------------------------------

# Dataset Management & Metadata

The Dataset Management module provides centralized dataset registration,
metadata management, lifecycle management, versioning, search,
filtering, and authorization.

## Dataset Metadata

Each dataset maintains:

-   Dataset Name
-   Dataset Code (unique)
-   Description
-   Workspace
-   Category
-   Owner
-   Data Source Name
-   Data Source Type
-   Data Source Description / Source Details
-   Tags
-   Status (`Draft`, `Active`, `Archived`)
-   Current Version
-   Created Date
-   Last Modified Date
-   Soft-delete information

Every dataset belongs to one workspace and is subject to workspace and
ownership authorization rules.

## Dataset Operations

Supported operations include:

-   Create/Register Dataset
-   Update Dataset Metadata
-   Get Dataset by ID
-   List Datasets
-   Search and Filter Datasets
-   Activate Dataset
-   Archive Dataset
-   Restore Dataset lifecycle state
-   Soft Delete Dataset
-   Recover Soft Deleted Dataset
-   View Version History
-   View a Previous Version
-   Restore a Previous Version

Soft delete retains the dataset record instead of physically removing
it. Deleted datasets can be recovered by an authorized user.

------------------------------------------------------------------------

# Dataset Versioning

Dataset metadata is versioned.

Capabilities include:

-   Initial version creation
-   Automatic version increment when versioned metadata is updated
-   Current version identification
-   Version history
-   Version number and timestamp tracking
-   Version notes
-   Read-only access to previous versions
-   Restore a previous version
-   Complete metadata version audit trail

Restoring a previous version creates a new current version rather than
overwriting historical records.

------------------------------------------------------------------------

# Search & Filter

Dataset discovery supports filtering/searching by:

-   Dataset Name / keyword
-   Category
-   Workspace
-   Owner
-   Status
-   Tags
-   Created Date Range

The list/search APIs may also support pagination and sorting.

------------------------------------------------------------------------

# Role-Based Dataset Access

Dataset operations are protected using RBAC together with workspace and
ownership checks.

Expected access model:

-   **Platform Administrator** -- can manage datasets across the
    platform.
-   **Workspace Administrator** -- can manage datasets within the
    authorized workspace.
-   **Data Analyst** -- can create/update datasets according to assigned
    workspace and ownership rules; ownership restrictions prevent
    unauthorized assignment to another user.
-   **Business User / Viewer** -- read-only dataset access where the
    assigned permissions allow it.

Unauthorized operations return the appropriate `401 Unauthorized` or
`403 Forbidden` response.

------------------------------------------------------------------------

# Dataset Validation Rules

Dataset APIs validate:

-   Mandatory fields
-   Unique Dataset Code
-   Maximum supported field lengths
-   Valid workspace
-   Valid category
-   Active owner
-   Owner/workspace relationship
-   Valid status and status transitions
-   Duplicate tag prevention
-   Valid dataset identifier/version number
-   Authorization and workspace boundaries

Validation failures return appropriate `400 Bad Request`,
`403 Forbidden`, `404 Not Found`, or conflict responses according to the
condition.

------------------------------------------------------------------------

# Database Design

The dataset catalog uses normalized tables including:

-   `Datasets`
-   `DatasetVersions`
-   `DatasetCategories`
-   `Tags`
-   `DatasetTags`

Related platform tables include:

-   `Workspaces`
-   `AspNetUsers`
-   `AspNetRoles`
-   `AspNetUserRoles`
-   `Permissions`
-   `RolePermissions`
-   `AuditLogs`
-   `RefreshTokens`
-   `__EFMigrationsHistory`

### Dataset Relationships

``` text
Workspaces ───────< Datasets >─────── DatasetCategories
                     │
                     ├──────── Owner (AspNetUsers)
                     │
                     ├────────< DatasetVersions
                     │
                     └────────< DatasetTags >──────── Tags
```

`DatasetTags` provides the many-to-many relationship between datasets
and tags. `DatasetVersions` stores historical metadata snapshots/version
information.

------------------------------------------------------------------------

# Audit Logging

The platform records important activities including:

-   Login / Logout
-   Password Changes
-   Workspace Updates
-   User Management
-   Role Assignment
-   Dataset Creation
-   Dataset Updates
-   Dataset Version Changes
-   Archive / Restore
-   Soft Delete / Recovery

------------------------------------------------------------------------

# Technology Stack

-   .NET 8
-   ASP.NET Core Web API
-   ASP.NET Core Identity
-   Entity Framework Core
-   SQL Server
-   JWT Authentication
-   Swagger / OpenAPI
-   xUnit

------------------------------------------------------------------------

# Project Architecture

``` text
EnterpriseDataIntelligencePlatform
│
├── Authorization
├── Contracts
├── Controllers
├── Data
├── Domain
├── Extensions
├── Infrastructure
├── Middleware
├── Migrations
├── Services
│   ├── Interfaces
│   └── Implementations
├── Docs
├── Postman
└── EnterpriseDataIntelligencePlatform.Tests
```

### Request Flow

``` text
Client
  ↓
Controller
  ↓
Service Layer
  ↓
Entity Framework Core
  ↓
SQL Server
  ↓
Response
```

------------------------------------------------------------------------

# Main API Modules

## Authentication

-   Login
-   Refresh Token
-   Logout
-   Change Password
-   Forgot Password
-   Reset Password

## Workspaces

-   Create
-   Update
-   Activate
-   Deactivate
-   View Details

## Users

-   Register
-   Update
-   Activate
-   Deactivate
-   Assign Role
-   Search

## Datasets

The Dataset API includes operations for:

-   Create Dataset
-   Update Dataset
-   Get Dataset by ID
-   List/Search Datasets
-   Soft Delete Dataset
-   Activate Dataset
-   Archive Dataset
-   Restore Dataset
-   Recover Soft Deleted Dataset
-   Get Dataset Version History
-   Get/View a Specific Previous Version
-   Restore a Previous Version

Representative routes are:

``` text
POST    /api/datasets
GET     /api/datasets
GET     /api/datasets/{id}
PUT     /api/datasets/{id}
DELETE  /api/datasets/{id}

PATCH   /api/datasets/{id}/activate
PATCH   /api/datasets/{id}/archive
PATCH   /api/datasets/{id}/restore
PATCH   /api/datasets/{id}/recover

GET     /api/datasets/{id}/versions
GET     /api/datasets/{id}/versions/{versionNumber}
```

Use the generated Swagger/OpenAPI document as the authoritative source
for the exact request models and any additional routes in the current
build.

## Dataset Categories

Dataset categories provide normalized classification metadata used by
datasets and search/filter operations.

------------------------------------------------------------------------

# Configuration

Configure the required sections inside **appsettings.json**, including:

-   ConnectionStrings
-   Jwt
-   DefaultAdmin
-   PasswordReset

For production environments, store secrets using User Secrets, Azure Key
Vault, or environment variables.

------------------------------------------------------------------------

# Database Setup

``` bash
dotnet restore
dotnet ef database update --project EnterpriseDataIntelligencePlatform
```

Before applying migrations to an existing database, verify the
configured connection string and review pending migrations.

------------------------------------------------------------------------

# Running the Application

``` bash
dotnet restore
dotnet build
dotnet ef database update --project EnterpriseDataIntelligencePlatform
dotnet run --project EnterpriseDataIntelligencePlatform
```

Open Swagger using the HTTPS URL configured by the current launch
profile.

------------------------------------------------------------------------

# Swagger / API Documentation

Swagger/OpenAPI provides the current API definitions, request schemas,
response schemas, and secured endpoint testing.

Typical secured API testing flow:

1.  Login using the Authentication API.
2.  Copy the returned JWT access token.
3.  Click **Authorize** in Swagger.
4.  Enter the bearer token as required by the Swagger security
    configuration.
5.  Test the secured APIs.

Request/response examples used for project verification should also be
maintained in the Postman collection and supporting technical
documentation.

------------------------------------------------------------------------

# Testing

Recommended dataset verification includes:

-   Dataset creation
-   Dataset retrieval
-   Dataset metadata update
-   Dataset listing
-   Dataset search and every required filter
-   Unique Dataset Code validation
-   Mandatory-field validation
-   Maximum-length validation
-   Invalid status transition validation
-   Duplicate tag prevention
-   Workspace/owner validation
-   Role-based authorization
-   Cross-workspace authorization
-   Automatic version increment
-   Version history
-   Version timestamps
-   Read-only previous-version retrieval
-   Previous-version restoration
-   Archive / Restore
-   Soft Delete
-   Soft-delete verification
-   Recover deleted dataset
-   Audit logging

Run automated tests with:

``` bash
dotnet test
```

------------------------------------------------------------------------

# Dataset Test Flow

A representative end-to-end test is:

``` text
Login
  ↓
Obtain Access Token
  ↓
Create/Identify Workspace
  ↓
Create/Identify Authorized User
  ↓
Create Dataset
  ↓
Verify Initial Version
  ↓
Update Metadata
  ↓
Verify Automatic Version Increment
  ↓
Get Version History
  ↓
View Previous Version
  ↓
Search / Filter
  ↓
Test Role and Workspace Restrictions
  ↓
Archive / Restore
  ↓
Soft Delete
  ↓
Verify Dataset Is Hidden From Normal Retrieval
  ↓
Recover
  ↓
Verify Dataset Is Available Again
```

------------------------------------------------------------------------

# Documentation Deliverables

The implementation documentation should include:

-   Entity Relationship Diagram (ERD)
-   API documentation
-   Request/response samples
-   Validation rules
-   Role/permission and workspace assumptions
-   Dataset lifecycle assumptions
-   Versioning assumptions
-   Postman test scenarios

------------------------------------------------------------------------

# Supporting Documents

### Docs

-   Technical documentation / ERD and implementation notes

### Postman

-   Postman collection containing authentication, dataset, versioning,
    validation, filtering, lifecycle, and RBAC scenarios

### SQL

-   Database initialization/setup script and EF Core migrations
    applicable to the current solution

------------------------------------------------------------------------

# Security Notes

-   Never commit production secrets.
-   Replace development JWT keys before deployment.
-   Replace default administrator passwords.
-   Enable HTTPS in production.
-   Restrict CORS appropriately.
-   Store secrets securely.
-   Enable database backups and monitoring.
-   Enforce workspace isolation and authorization on every protected
    dataset operation.

------------------------------------------------------------------------

This README provides the primary project overview and documents the
Dataset Management, Versioning, Metadata, Search/Filter, RBAC,
Validation, API, and database capabilities required by the current
implementation.

## Task 17 – Data Import & Ingestion Management

Task 17 adds secure CSV/XLSX ingestion with local file storage abstraction, Excel worksheet selection, first-import schema inference, staging tables, background Channel/HostedService processing, Full/Append modes, configurable duplicate handling, row-level errors, cancellation, workspace isolation, audit events and import-specific RBAC.

See `Docs/TASK17_DATA_IMPORT_INGESTION.md` for architecture and API examples.
# Task 18: Data Transformation, Mapping and Validation

## Architecture and assumptions

Transformation configuration is dataset-level and reusable. Each save creates an immutable integer version; exactly one version is active. An import captures both `TransformationConfigurationId` and `TransformationConfigurationVersion` when it is created, so later edits cannot change a running import. Mappings can target only existing `DatasetColumn` records and target conversion follows that column's data type.

The processing order is `Upload -> Stage -> Map -> Default -> ordered Transformations -> ordered Validations -> KeyHash duplicate validation -> Process -> Commit`. Record-level failures are written to the existing `ImportErrors` store and valid rows continue, producing `Completed With Errors`. A critical exception rolls back the commit transaction and preserves the previous full-import dataset. Preview uses `Stage -> Map -> Transform -> Validate -> Preview`, accepts an existing `ImportId`, is limited to 100 rows, and never writes final records.

Configurations contain source-to-target mappings, a default, ordered transformations, and ordered validations. Supported transformations are Trim, Uppercase, Lowercase, Replace, Default, DateFormat, Numeric, StringLength, and a safe template-based Derived field (`{ColumnName}` placeholders). Supported validations are Required, DataType, MaximumLength, MinimumLength, NumericRange, DateRange, AllowedValues, Pattern, and Duplicate. Duplicate validation deliberately delegates to the Task 17 `KeyColumns`/`KeyHash` pipeline.

Errors are categorized as Transformation Error, Validation Error, Duplicate Error, or Processing Error and retain import, dataset through the import relationship, row, field, original value, rule, and message. Existing dataset/workspace query filters and permissions enforce isolation. Dataset update permission manages configurations; dataset/import view permissions provide read-only configuration, history, preview, and result access. All configuration saves/deletes and executions use the existing audit service.

## API examples

Create a new active configuration version:

```http
POST /api/datasets/{datasetId}/transformation-configurations
Authorization: Bearer {token}
Content-Type: application/json

{
  "mappings": [{
    "sourceColumn": " employee_name ",
    "targetColumnId": "00000000-0000-0000-0000-000000000001",
    "isRequired": true,
    "defaultValue": "UNKNOWN",
    "transformations": [
      { "type": "Trim", "sequence": 1, "parameters": null },
      { "type": "Uppercase", "sequence": 2, "parameters": null }
    ],
    "validations": [
      { "type": "MaximumLength", "sequence": 1, "message": "Name is too long", "parameters": { "value": "100" } }
    ]
  }]
}
```

```http
GET /api/datasets/{datasetId}/transformation-configurations/active
GET /api/datasets/{datasetId}/transformation-configurations/history
DELETE /api/datasets/{datasetId}/transformation-configurations/{configurationId}
POST /api/imports/{importId}/transformation-preview

{ "limit": 25 }
```

Extension strategy: add a new constant and a handler branch (or extract a dedicated strategy implementing `ITransformationEngine`) without changing persistence or pipeline contracts. Rule parameters are stored with the immutable version, preserving reproducibility.
