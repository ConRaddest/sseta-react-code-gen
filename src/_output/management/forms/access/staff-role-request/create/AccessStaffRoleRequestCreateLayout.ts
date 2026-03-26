import { FormLayout } from "@sseta/components"

const AccessStaffRoleRequestCreateLayout: FormLayout[] = [
  {
    groupName: "Additional Fields",
    totalColumns: 2,
    fields: [
      { name: "requestReason", columns: 1, heading: "Request Reason" },
      { name: "name", columns: 1, heading: "Name" },
      { name: "rejectionReason", columns: 1, heading: "Rejection Reason" },
      { name: "reviewedOn", columns: 1, heading: "Reviewed On", type: "datetime" },
    ],
  },
]

export default AccessStaffRoleRequestCreateLayout
