import { FormLayout } from "@sseta/components"

const PmvrSystemUserUpdateLayout: FormLayout[] = [
  {
    groupName: "Basic Information",
    totalColumns: 2,
    fields: [
      { name: "mobileNumber", columns: 1, heading: "Mobile Number", type: "phone" },
    ],
  },
]

export default PmvrSystemUserUpdateLayout
