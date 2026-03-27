// !!---------------------------------------------------------!!
// !!-------- AUTO-GENERATED: Edit in code generator! --------!!
// !!--------------- CHANGES HERE WILL BE LOST ---------------!!
// !!---------------------------------------------------------!!

import { FormLayout } from "@sseta/components"

export default function useAccessStaffRoleRequestViewFields() {
  const layout: FormLayout[] = [
    {
      groupName: "Additional Fields",
      totalColumns: 2,
      fields: [
        { name: "reviewedOn", columns: 1, heading: "Reviewed On", type: "datetime" },
        { name: "createdOn", columns: 1, heading: "Created On", type: "datetime" },
        { name: "modifiedOn", columns: 1, heading: "Modified On", type: "datetime" },
        { name: "deletedOn", columns: 1, heading: "Deleted On", type: "datetime" },
        { name: "name", columns: 1, heading: "Name" },
        { name: "requestedBySystemUserName", columns: 1, heading: "Requested By System User" },
        { name: "reviewedBySystemUserName", columns: 1, heading: "Reviewed By System User" },
        { name: "requestReason", columns: 1, heading: "Request Reason" },
        { name: "rejectionReason", columns: 1, heading: "Rejection Reason" },
        { name: "roleName", columns: 1, heading: "Role" },
        { name: "statusName", columns: 1, heading: "Status" },
        { name: "createdBySystemUserName", columns: 1, heading: "Created By System User" },
        { name: "modifiedBySystemUserName", columns: 1, heading: "Modified By System User" },
        { name: "deletedBySystemUserName", columns: 1, heading: "Deleted By System User" },
      ],
    },
  ]

  return { layout }
}

