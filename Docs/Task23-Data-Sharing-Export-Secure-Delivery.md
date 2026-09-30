# Task 23 – Data Sharing, Export & Secure Delivery

## Implemented scope

- CSV and Excel dataset exports.
- Dataset/version selection, selected columns, filters, sorting, optional page/page-size and batched record processing.
- Existing workspace, RBAC and `DatasetAccessGrant` authorization is reused.
- Confidential and Restricted datasets require an active approved grant through the existing access policy.
- Archived datasets are never exportable or shareable.
- Synchronous processing is used for exports with up to 10,000 matching records; larger exports are queued through the existing background job queue.
- Generated files use application-managed local storage behind `IExportStorageService` and are retained for 24 hours.
- Download APIs never expose physical storage paths and revalidate dataset authorization at download time.
- Export request/history records preserve filters, status, record count, timestamps and failures.
- Dataset sharing reuses `DatasetAccessGrant`; a controlled approved `DatasetAccessRequest` is created as the grant's source when a direct share creates a new grant.
- Share history is retained separately for shared/updated/revoked actions.
- Cross-workspace sharing is rejected.
- A sharer cannot grant a higher access level than their effective dataset access.
- Existing `IAuditService` records export request/completion/failure/download and share/revoke activity.
- Export dashboard and paginated history APIs are included.

## APIs

### Exports

- `POST /api/data-sharing/exports`
- `GET /api/data-sharing/exports/{exportId}`
- `POST /api/data-sharing/exports/{exportId}/cancel`
- `GET /api/data-sharing/exports`
- `GET /api/data-sharing/exports/{exportId}/download`
- `GET /api/data-sharing/exports/dashboard`

### Sharing

- `POST /api/data-sharing/shares`
- `PUT /api/data-sharing/shares/{grantId}`
- `POST /api/data-sharing/shares/{grantId}/revoke`
- `GET /api/data-sharing/shares`

## Storage

Default storage root: `App_Data/Exports`.

Files are addressed by an opaque export identifier and never by a client-supplied physical path. A cleanup background service removes expired files after the 24-hour retention period.

## Database

Migration:

`20260924090000_Task23DataSharingExportSecureDelivery`

Tables:

- `DataExportRequests`
- `DatasetShareHistories`

`DatasetAccessGrant` remains the effective dataset authorization mechanism.

## Security flow

`Authentication → Workspace/RBAC → DatasetAccessGrant → Governance/Classification → Operation`

For download, the flow is evaluated again, so revoking access after an export was generated prevents subsequent restricted downloads.
