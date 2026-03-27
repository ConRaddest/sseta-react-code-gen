// !!---------------------------------------------------!!
// !!---------- AUTO-GENERATED: Do not edit! -----------!!
// !!---------------------------------------------------!!

import { FieldErrors } from "react-hook-form"
import { FormLayout } from "@sseta/components"
import { AdminStaffRoleRequestUpdateRequest } from "@/types/api.types"

interface UseAdminStaffRoleRequestUpdateFieldsProps {
  errors: FieldErrors<AdminStaffRoleRequestUpdateRequest>
  disabledFields?: string[]
}

export default function useAdminStaffRoleRequestUpdateFields(props: UseAdminStaffRoleRequestUpdateFieldsProps) {
  const { errors, disabledFields = [] } = props

  const fields = {
    rejectionReason: {
      props: {
        id: "staff-role-request-rejectionReason",
        name: "rejectionReason",
        heading: "Rejection Reason",
        type: "text" as const,
        placeholder: "Enter rejection reason...",
        disabled: disabledFields.includes("rejectionReason"),
        error: errors.rejectionReason,
      },
      rules: {
        maxLength: { value: 250, message: "Must be less than 250 characters." },
      },
    },
  }

  const layout: FormLayout[] = [
    {
      groupName: "Additional Fields",
      totalColumns: 2,
      fields: [
        { name: "rejectionReason", columns: 1, heading: "Rejection Reason" },
      ],
    },
  ]

  return { fields, layout }
}

