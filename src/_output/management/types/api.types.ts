// !!---------------------------------------------------------!!
// !!-------- AUTO-GENERATED: Edit in code generator! --------!!
// !!--------------- CHANGES HERE WILL BE LOST ---------------!!
// !!---------------------------------------------------------!!

// ---- Auth ----

export interface AuthProfileResponse {
  systemUserId: number
  createdOn: string
  modifiedOn: string
  deletedOn: string | null
  systemUserTypeId: number
  personId: number | null
  systemUserTypeName: string | null
  createdBySystemUserId: number
  createdBySystemUserName: string | null
  modifiedBySystemUserId: number
  modifiedBySystemUserName: string | null
  deletedBySystemUserId: number | null
  deletedBySystemUserName: string | null
  email: string | null
  mobileNumber: string | null
  firstName: string | null
  lastName: string | null
  ssoId: string | null
  mfaTypeId: number
  mfaTypeName: string | null
  roles: AuthProfileResponseRole[] | null
  permissions: string[] | null
}

export interface AuthProfileResponseRole {
  roleId: number
  roleName: string | null
}

// ======== ACCESS ========

// -- ACCESS / DepartmentType --

export interface AccessDepartmentTypeSearchResponse {
  departmentTypeId: number
  name: string | null
}

// -- ACCESS / StaffRoleRequest --

export interface AccessStaffRoleRequestCreateRequest {
  requestedBySystemUserId: number
  requestReason: string
  roleId: number
  statusId: number
  statusReason: string
  name?: string | null
  reviewedBySystemUserId?: number | null
  rejectionReason?: string | null
  reviewedOn?: string | null
}

export interface AccessStaffRoleRequestCreateResponse {
  staffRoleRequestId: number | null
}

export interface AccessStaffRoleRequest {
  reviewedOn: string | null
  createdOn: string
  modifiedOn: string
  deletedOn: string | null
  staffRoleRequestId: number
  requestedBySystemUserId: number
  reviewedBySystemUserId: number | null
  roleId: number
  statusId: number
  createdBySystemUserId: number
  modifiedBySystemUserId: number
  deletedBySystemUserId: number | null
  name: string | null
  requestedBySystemUserName: string | null
  reviewedBySystemUserName: string | null
  requestReason: string | null
  rejectionReason: string | null
  roleName: string | null
  statusName: string | null
  statusReason: string | null
  createdBySystemUserName: string | null
  modifiedBySystemUserName: string | null
  deletedBySystemUserName: string | null
}

export interface AccessStaffRoleRequestSearchResponse {
  reviewedOn: string | null
  createdOn: string
  modifiedOn: string
  deletedOn: string | null
  staffRoleRequestId: number
  requestedBySystemUserId: number
  reviewedBySystemUserId: number | null
  roleId: number
  statusId: number
  createdBySystemUserId: number
  modifiedBySystemUserId: number
  deletedBySystemUserId: number | null
  name: string | null
  requestedBySystemUserName: string | null
  reviewedBySystemUserName: string | null
  requestReason: string | null
  rejectionReason: string | null
  roleName: string | null
  statusName: string | null
  statusReason: string | null
  createdBySystemUserName: string | null
  modifiedBySystemUserName: string | null
  deletedBySystemUserName: string | null
}

// ======== B ========

// -- B / Role --

export interface BRoleSearchResponse {
  roleId: number
  name: string | null
}

// ======== ECD ========

// -- ECD / Role --

export interface EcdRoleSearchResponse {
  roleId: number
  name: string | null
}

// -- ECD / SystemUser --

export interface EcdSystemUserUpdateRequest {
  mobileNumber?: string | null
}

// ======== PMVR ========

// -- PMVR / Role --

export interface PmvrRoleSearchResponse {
  roleId: number
  name: string | null
}

// -- PMVR / SystemUser --

export interface PmvrSystemUserUpdateRequest {
  mobileNumber?: string | null
}

// ======== SP ========

// -- SP / Role --

export interface SpRoleSearchResponse {
  roleId: number
  name: string | null
}

// -- SP / SystemUser --

export interface SpSystemUserUpdateRequest {
  mobileNumber?: string | null
}

// ======== SPI ========

// -- SPI / Role --

export interface SpiRoleSearchResponse {
  roleId: number
  name: string | null
}

// -- SPI / SystemUser --

export interface SpiSystemUserUpdateRequest {
  mobileNumber?: string | null
}

// ======== ADMIN ========

// -- ADMIN / StaffRoleRequest --

export interface AdminStaffRoleRequest {
  reviewedOn: string | null
  createdOn: string
  modifiedOn: string
  deletedOn: string | null
  staffRoleRequestId: number
  requestedBySystemUserId: number
  reviewedBySystemUserId: number | null
  roleId: number
  statusId: number
  createdBySystemUserId: number
  modifiedBySystemUserId: number
  deletedBySystemUserId: number | null
  name: string | null
  requestedBySystemUserName: string | null
  reviewedBySystemUserName: string | null
  requestReason: string | null
  rejectionReason: string | null
  roleName: string | null
  statusName: string | null
  statusReason: string | null
  createdBySystemUserName: string | null
  modifiedBySystemUserName: string | null
  deletedBySystemUserName: string | null
}

export interface AdminStaffRoleRequestSearchResponse {
  reviewedOn: string | null
  createdOn: string
  modifiedOn: string
  deletedOn: string | null
  staffRoleRequestId: number
  requestedBySystemUserId: number
  reviewedBySystemUserId: number | null
  roleId: number
  statusId: number
  createdBySystemUserId: number
  modifiedBySystemUserId: number
  deletedBySystemUserId: number | null
  name: string | null
  requestedBySystemUserName: string | null
  reviewedBySystemUserName: string | null
  requestReason: string | null
  rejectionReason: string | null
  roleName: string | null
  statusName: string | null
  statusReason: string | null
  createdBySystemUserName: string | null
  modifiedBySystemUserName: string | null
  deletedBySystemUserName: string | null
}

export interface AdminStaffRoleRequestSubmitRequest {
  staffRoleRequestId: number
  targetStatusId: number
}

export interface AdminStaffRoleRequestUpdateRequest {
  staffRoleRequestId: number
  rejectionReason?: string | null
}

export interface AdminStaffRoleRequestValidateRequest {
  staffRoleRequestId: number
  targetStatusId: number
  suppressValidationStepResults: boolean
}