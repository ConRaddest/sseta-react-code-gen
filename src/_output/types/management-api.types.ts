// !!---------------------------------------------------------!!
// !!-------- AUTO-GENERATED: Edit in code generator! --------!!
// !!--------------- CHANGES HERE WILL BE LOST ---------------!!
// !!---------------------------------------------------------!!

// ---- Auth ----

export interface AUTH_ProfileResponse {
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
  roles: AUTH_ProfileResponseRole[] | null
  permissions: string[] | null
}

export interface AUTH_ProfileResponseRole {
  roleId: number
  roleName: string | null
}

// ======== ADMIN ========

// -- ADMIN / DepartmentType --

export interface ADMIN_DepartmentType {
  departmentTypeId: number
  name: string | null
}

// -- ADMIN / StaffRoleRequest --

export interface ADMIN_StaffRoleRequest {
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

export interface ADMIN_StaffRoleRequestUpdateRequest {
  staffRoleRequestId: number
  rejectionReason: string | null
}

// ======== B ========

// -- B / Role --

export interface B_Role {
  roleId: number
  name: string | null
}

// -- B / StaffRoleRequest --

export interface B_StaffRoleRequestCreateRequest {
  requestedBySystemUserId: number
  requestReason: string
  roleId: number
  statusId: number
  statusReason: string
  name: string | null
  reviewedBySystemUserId: number | null
  rejectionReason: string | null
  reviewedOn: string | null
}

export interface B_StaffRoleRequestCreateResponse {
  staffRoleRequestId: number | null
}

// ======== ECD ========

// -- ECD / Role --

export interface ECD_Role {
  roleId: number
  name: string | null
}

// -- ECD / StaffRoleRequest --

export interface ECD_StaffRoleRequestCreateRequest {
  requestedBySystemUserId: number
  requestReason: string
  roleId: number
  statusId: number
  statusReason: string
  name: string | null
  reviewedBySystemUserId: number | null
  rejectionReason: string | null
  reviewedOn: string | null
}

export interface ECD_StaffRoleRequestCreateResponse {
  staffRoleRequestId: number | null
}

// -- ECD / SystemUser --

export interface ECD_SystemUserUpdateRequest {
  mobileNumber: string | null
}

// ======== PMVR ========

// -- PMVR / Role --

export interface PMVR_Role {
  roleId: number
  name: string | null
}

// -- PMVR / StaffRoleRequest --

export interface PMVR_StaffRoleRequestCreateRequest {
  requestedBySystemUserId: number
  requestReason: string
  roleId: number
  statusId: number
  statusReason: string
  name: string | null
  reviewedBySystemUserId: number | null
  rejectionReason: string | null
  reviewedOn: string | null
}

export interface PMVR_StaffRoleRequestCreateResponse {
  staffRoleRequestId: number | null
}

// -- PMVR / SystemUser --

export interface PMVR_SystemUserUpdateRequest {
  mobileNumber: string | null
}

// ======== SP ========

// -- SP / Role --

export interface SP_Role {
  roleId: number
  name: string | null
}

// -- SP / StaffRoleRequest --

export interface SP_StaffRoleRequestCreateRequest {
  requestedBySystemUserId: number
  requestReason: string
  roleId: number
  statusId: number
  statusReason: string
  name: string | null
  reviewedBySystemUserId: number | null
  rejectionReason: string | null
  reviewedOn: string | null
}

export interface SP_StaffRoleRequestCreateResponse {
  staffRoleRequestId: number | null
}

// -- SP / SystemUser --

export interface SP_SystemUserUpdateRequest {
  mobileNumber: string | null
}

// ======== SPI ========

// -- SPI / Role --

export interface SPI_Role {
  roleId: number
  name: string | null
}

// -- SPI / StaffRoleRequest --

export interface SPI_StaffRoleRequestCreateRequest {
  requestedBySystemUserId: number
  requestReason: string
  roleId: number
  statusId: number
  statusReason: string
  name: string | null
  reviewedBySystemUserId: number | null
  rejectionReason: string | null
  reviewedOn: string | null
}

export interface SPI_StaffRoleRequestCreateResponse {
  staffRoleRequestId: number | null
}

// -- SPI / SystemUser --

export interface SPI_SystemUserUpdateRequest {
  mobileNumber: string | null
}