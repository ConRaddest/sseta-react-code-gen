import { FieldErrors } from "react-hook-form"
import { AdminStaffRoleRequestUpdateRequest } from "@/types/api.types"

interface UseAdminStaffRoleRequestUpdateFieldsProps {
  errors: FieldErrors<AdminStaffRoleRequestUpdateRequest>
  disabledFields?: string[]
}

export default function useAdminStaffRoleRequestUpdateFields(props: UseAdminStaffRoleRequestUpdateFieldsProps) {
  const { errors, disabledFields = [] } = props

  return {
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
}
