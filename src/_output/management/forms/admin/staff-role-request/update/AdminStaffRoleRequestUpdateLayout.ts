import { FormLayout } from "@sseta/components"

const AdminStaffRoleRequestUpdateLayout: FormLayout[] = [
  {
    groupName: "Additional Fields",
    totalColumns: 2,
    fields: [
      { name: "rejectionReason", columns: 1, heading: "Rejection Reason" },
    ],
  },
]

export default AdminStaffRoleRequestUpdateLayout
